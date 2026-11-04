using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Webignite.Desktop.Files;

/// <summary>
/// A file's id on its drive, which Windows keeps when the file is renamed or moved on the same drive. Tags keep it,
/// so they find a file again after it was moved in Explorer.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileIdentity
{
    /// <summary>The id, as "volume-filenumber", or null when Windows can't say.</summary>
    public static string? Key(string path)
    {
        using var handle = Open(path);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            return null;
        return $"{info.VolumeSerialNumber:x8}-{info.FileIndexHigh:x8}{info.FileIndexLow:x8}";
    }

    /// <summary>
    /// Where a file with this id is now, looking on the drive of a path it had. Null when it's gone, on another
    /// drive, or in the Recycle Bin.
    /// </summary>
    public static string? Find(string key, string oldPath)
    {
        var parts = key.Split('-');
        if (parts.Length != 2 || !long.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var number)
            || Path.GetPathRoot(oldPath) is not { Length: > 0 } root)
            return null;
        using var volume = Open(root);
        if (volume.IsInvalid)
            return null;
        var id = new FileIdDescriptor { Size = Marshal.SizeOf<FileIdDescriptor>(), Type = 0, FileId = number };
        using var file = OpenFileById(volume, ref id, ReadAttributes, ShareAll, IntPtr.Zero, BackupSemantics);
        if (file.IsInvalid)
            return null;
        var name = new char[1024];
        var length = GetFinalPathNameByHandle(file, name, (uint)name.Length, 0);
        if (length == 0 || length >= name.Length)
            return null;
        var path = new string(name, 0, (int)length);
        path = path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        if (path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase))
            return null;
        return Key(path) == key ? path : null;
    }

    private static SafeFileHandle Open(string path) =>
        CreateFile(path, ReadAttributes, ShareAll, IntPtr.Zero, 3 /* OPEN_EXISTING */, BackupSemantics, IntPtr.Zero);

    private const uint ReadAttributes = 0x80;
    private const uint ShareAll = 0x7;
    private const uint BackupSemantics = 0x02000000; // needed to open folders

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(SafeFileHandle volumeHint, ref FileIdDescriptor id, uint access, uint share, IntPtr security, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

    // Pack 4: the three times are FILETIMEs, two 4-byte halves each, so no gap comes before them.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct FileInformation
    {
        public uint Attributes;
        public long Created;
        public long Accessed;
        public long Written;
        public uint VolumeSerialNumber;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Links;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct FileIdDescriptor
    {
        [FieldOffset(0)] public int Size;
        [FieldOffset(4)] public int Type;
        [FieldOffset(8)] public long FileId;
    }
}
