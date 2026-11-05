using System.Diagnostics;
using Webignite.Core.Files;

namespace Webignite.Desktop.Files;

/// <summary>
/// What the file explorer does to files on disk: reading folders, copying, moving, renaming, new folders and
/// the Recycle Bin. Nothing here is undone silently; every change the explorer makes is recorded so Ctrl+Z
/// can put it back.
/// </summary>
public static class FileOps
{
    /// <summary>A folder's files and folders. Hidden and system ones are left out, as Explorer does by default.</summary>
    public static IReadOnlyList<FileEntry> List(string folder)
    {
        var entries = new List<FileEntry>();
        var directory = new DirectoryInfo(folder);
        foreach (var item in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System }))
        {
            try
            {
                entries.Add(item is FileInfo file
                    ? new FileEntry(file.FullName, file.Name, false, file.Length, file.LastWriteTime)
                    : new FileEntry(item.FullName, item.Name, true, null, item.LastWriteTime));
            }
            catch (IOException)
            {
                // A file that vanished while the folder was being read.
            }
        }
        return entries;
    }

    /// <summary>One file or folder as the list shows it, or null when it isn't there.</summary>
    public static FileEntry? Entry(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                return new FileEntry(file.FullName, file.Name, false, file.Length, file.LastWriteTime);
            }
            if (Directory.Exists(path))
            {
                var folder = new DirectoryInfo(path);
                return new FileEntry(folder.FullName, folder.Name, true, null, folder.LastWriteTime);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>Where client folders live: the one set in Settings, else C:\Projects when it exists.</summary>
    public static string? ClientRoot(FileSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ClientRoot) && Directory.Exists(settings.ClientRoot))
            return settings.ClientRoot;
        return OperatingSystem.IsWindows() && Directory.Exists(@"C:\Projects") ? @"C:\Projects" : null;
    }

    /// <summary>The folders directly in a folder, or none when it can't be read.</summary>
    public static IReadOnlyList<string> Folders(string folder)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(folder)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The usual folders, in the order the side list shows them.</summary>
    public static IReadOnlyList<(string Name, string Path)> UsualFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        (string, string)[] folders =
        [
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Downloads", Path.Combine(home, "Downloads")),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Home", home),
        ];
        return [.. folders.Where(f => f.Item2.Length > 0 && Directory.Exists(f.Item2)).DistinctBy(f => f.Item2)];
    }

    /// <summary>Every drive that is ready: "Local Disk (C:)", "USB (E:)".</summary>
    public static IReadOnlyList<(string Name, string Path)> Drives()
    {
        var drives = new List<(string, string)>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.Ram or DriveType.Unknown)
                    continue;
                if (!OperatingSystem.IsWindows() && drive.RootDirectory.FullName != "/")
                    continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType == DriveType.Removable ? "USB drive" : "Local disk" : drive.VolumeLabel;
                drives.Add(OperatingSystem.IsWindows() ? ($"{label} ({drive.Name.TrimEnd('\\')})", drive.Name) : ("Computer", "/"));
            }
            catch (IOException)
            {
                // A drive that went away while being read.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return drives;
    }

    /// <summary>Copies files and folders into a folder. A name already there gets "(2)", or "- Copy" when copying into its own folder.</summary>
    public static IReadOnlyList<string> Copy(IEnumerable<string> paths, string into)
    {
        var made = new List<string>();
        foreach (var path in paths)
        {
            var isFolder = Directory.Exists(path);
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            var existing = Names(into);
            var sameFolder = string.Equals(Path.GetDirectoryName(path.TrimEnd('\\', '/')), into.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            var target = Path.Combine(into, sameFolder ? FileNames.CopyOf(name, isFolder, existing) : FileNames.Unique(name, isFolder, existing));
            if (isFolder)
            {
                if (IsInside(into, path))
                    throw new IOException($"{name} can't be copied into itself.");
                CopyFolder(path, target);
            }
            else
            {
                File.Copy(path, target);
            }
            made.Add(target);
        }
        return made;
    }

    /// <summary>Moves files and folders into a folder, each to a free name. Returns where each went.</summary>
    public static IReadOnlyList<(string From, string To)> Move(IEnumerable<string> paths, string into)
    {
        var moved = new List<(string, string)>();
        foreach (var path in paths)
        {
            var isFolder = Directory.Exists(path);
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (string.Equals(Path.GetDirectoryName(path.TrimEnd('\\', '/')), into.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                continue; // already there
            if (isFolder && IsInside(into, path))
                throw new IOException($"{name} can't be moved into itself.");
            var target = Path.Combine(into, FileNames.Unique(name, isFolder, Names(into)));
            MoveOne(path, target);
            moved.Add((path, target));
        }
        return moved;
    }

    // EXDEV: what Linux and macOS say when a folder can't simply be renamed onto another disk.
    private const int CrossDevice = 18;

    private static void CopyAcross(string from, string to)
    {
        CopyFolder(from, to);
        Directory.Delete(from, recursive: true);
    }

    /// <summary>Moves one file or folder to exactly this path, across drives too.</summary>
    public static void MoveOne(string from, string to)
    {
        if (Directory.Exists(from))
        {
            if (!string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
            {
                CopyAcross(from, to);
                return;
            }
            try
            {
                Directory.Move(from, to);
            }
            catch (IOException e) when (e.HResult == CrossDevice && !OperatingSystem.IsWindows())
            {
                // Outside Windows every path starts at /, so another disk only shows here, e.g. /tmp on its own
                // disk and the Trash in the home folder.
                CopyAcross(from, to);
            }
        }
        else
        {
            File.Move(from, to);
        }
    }

    /// <summary>Renames in place. Returns the new path.</summary>
    public static string Rename(string path, string newName)
    {
        if (FileNames.Problem(newName) is { } problem)
            throw new IOException(problem);
        var folder = Path.GetDirectoryName(path.TrimEnd('\\', '/'))!;
        var target = Path.Combine(folder, newName.Trim());
        if (string.Equals(path, target, StringComparison.Ordinal))
            return path;
        // Only the case changes: Windows needs a step through another name.
        if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
        {
            var step = target + ".renaming";
            MoveOne(path, step);
            MoveOne(step, target);
            return target;
        }
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"There is already something called {newName.Trim()} here.");
        MoveOne(path, target);
        return target;
    }

    /// <summary>
    /// Renames several at once. Each goes to a passing name first, so names can swap ("Page 1" and "Page 2") without
    /// one getting in the other's way. If one fails, those done so far are put back.
    /// </summary>
    public static void RenameMany(IReadOnlyList<(string From, string To)> renames)
    {
        var steps = renames.Select(r => (r.From, Step: r.From.TrimEnd('\\', '/') + "." + Guid.NewGuid().ToString("N")[..8] + ".renaming", r.To)).ToList();
        var done = new List<(string From, string To)>();
        try
        {
            foreach (var (from, step, _) in steps)
            {
                MoveOne(from, step);
                done.Add((from, step));
            }
            foreach (var (_, step, to) in steps)
            {
                MoveOne(step, to);
                done.Add((step, to));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            for (var i = done.Count - 1; i >= 0; i--)
                MoveOne(done[i].To, done[i].From);
            throw;
        }
    }

    public static string NewFolder(string into)
    {
        var path = Path.Combine(into, FileNames.NewFolder(Names(into)));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Sends files and folders to the Recycle Bin, so they can be got back from there. Outside Windows (for the
    /// screenshot tool) they go to the desktop's Trash folder instead.
    /// </summary>
    public static void Recycle(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (OperatingSystem.IsWindows())
            {
                if (Directory.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Trash", "files");
                Directory.CreateDirectory(trash);
                var name = Path.GetFileName(path.TrimEnd('/'));
                MoveOne(path, Path.Combine(trash, FileNames.Unique(name, Directory.Exists(path), Names(trash))));
            }
        }
    }

    /// <summary>Opens a file in its usual program, or a folder in Explorer.</summary>
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private static IEnumerable<string> Names(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder).Select(p => Path.GetFileName(p)) : [];

    private static bool IsInside(string folder, string maybeParent)
    {
        var a = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(maybeParent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(from))
            CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
}

/// <summary>A change the explorer made, kept so Ctrl+Z can put it back.</summary>
public abstract record FileChange(string What)
{
    public sealed record Renamed(string From, string To) : FileChange($"Renamed {Path.GetFileName(From)}");
    public sealed record Moved(IReadOnlyList<(string From, string To)> Items) : FileChange(Items.Count == 1 ? $"Moved {Path.GetFileName(Items[0].From)}" : $"Moved {Items.Count} items");
    public sealed record Made(IReadOnlyList<string> Paths) : FileChange(Paths.Count == 1 ? $"Made {Path.GetFileName(Paths[0])}" : $"Made {Paths.Count} items");
    public sealed record RenamedMany(IReadOnlyList<(string From, string To)> Items) : FileChange($"Renamed {Items.Count} items");
    public sealed record Rotated(IReadOnlyList<string> Paths, int Degrees) : FileChange(Paths.Count == 1 ? $"Turned {Path.GetFileName(Paths[0])}" : $"Turned {Paths.Count} pictures");

    /// <summary>Puts it back: a rename or move the other way; anything made (a copy, a new folder) to the Recycle Bin.</summary>
    public void Undo()
    {
        switch (this)
        {
            case Renamed r:
                FileOps.MoveOne(r.To, r.From);
                break;
            case Moved m:
                foreach (var (from, to) in m.Items.Reverse())
                    FileOps.MoveOne(to, from);
                break;
            case RenamedMany many:
                FileOps.RenameMany([.. many.Items.Select(i => (i.To, i.From))]);
                break;
            case Rotated rotated:
                QuickActionRunner.RotateNow(rotated.Paths, -rotated.Degrees);
                break;
            case Made made:
                FileOps.Recycle(made.Paths.Where(p => File.Exists(p) || Directory.Exists(p)));
                break;
        }
    }
}

/// <summary>What was copied or cut in Files. Copying also puts the files on Windows' clipboard, for pasting elsewhere.</summary>
public sealed class FileClipboard
{
    public IReadOnlyList<string> Paths { get; private set; } = [];
    public bool Cut { get; private set; }

    public void Set(IReadOnlyList<string> paths, bool cut)
    {
        Paths = paths;
        Cut = cut;
    }

    public void Clear() => Set([], false);

    public bool IsCut(string path) => Cut && Paths.Contains(path, StringComparer.OrdinalIgnoreCase);
}
