using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Webignite.Desktop.Files;

/// <summary>
/// What only Windows itself can do with a file: Open with, Properties, Show in Explorer, and its own right-click
/// menu ("More options"), so the extras other programs add there, such as 7-Zip, still work from the app.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsShell
{
    /// <summary>Windows' "Open with" box for a file.</summary>
    public static void OpenWith(string path) =>
        Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}") { UseShellExecute = false });

    /// <summary>Opens Explorer with the file picked.</summary>
    public static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });

    /// <summary>Windows' Properties window for a file or folder.</summary>
    public static void Properties(string path)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            lpVerb = "properties",
            lpFile = path,
            nShow = 5,
            fMask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
        };
        ShellExecuteEx(ref info);
    }

    /// <summary>
    /// Shows Windows' own right-click menu for these items (all in one folder) at a point on screen, and does
    /// what is picked there. Returns false when the menu couldn't be made.
    /// </summary>
    public static bool ShowMenu(IReadOnlyList<string> paths, IntPtr window, int x, int y)
    {
        if (paths.Count == 0)
            return false;
        var pidls = new List<IntPtr>();
        IntPtr menu = IntPtr.Zero;
        try
        {
            IShellFolder? parent = null;
            var children = new IntPtr[paths.Count];
            for (var i = 0; i < paths.Count; i++)
            {
                if (SHParseDisplayName(paths[i], IntPtr.Zero, out var pidl, 0, out _) != 0)
                    return false;
                pidls.Add(pidl);
                var shellFolderId = typeof(IShellFolder).GUID;
                if (SHBindToParent(pidl, ref shellFolderId, out var folder, out var child) != 0)
                    return false;
                parent ??= folder;
                children[i] = child;
            }
            var contextMenuId = typeof(IContextMenu).GUID;
            if (parent!.GetUIObjectOf(window, (uint)children.Length, children, ref contextMenuId, IntPtr.Zero, out var found) != 0
                || found is not IContextMenu contextMenu)
                return false;
            menu = CreatePopupMenu();
            const uint First = 1, Last = 0x7FFF, Normal = 0, Explore = 0x4;
            contextMenu.QueryContextMenu(menu, 0, First, Last, Normal | Explore);
            var picked = TrackPopupMenuEx(menu, 0x0100 /* TPM_RETURNCMD */, x, y, window, IntPtr.Zero);
            if (picked == 0)
                return true;
            var invoke = new CommandInfo
            {
                cbSize = Marshal.SizeOf<CommandInfo>(),
                hwnd = window,
                lpVerb = (IntPtr)(picked - First),
                nShow = 1,
            };
            contextMenu.InvokeCommand(ref invoke);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
                DestroyMenu(menu);
            foreach (var pidl in pidls)
                Marshal.FreeCoTaskMem(pidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder, out IntPtr childPidl);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr parameter, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr owner, ref Guid riid, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr owner, uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref Guid riid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr result);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInfo info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint length);
    }
}
