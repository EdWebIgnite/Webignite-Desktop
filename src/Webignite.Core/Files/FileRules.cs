using System.Globalization;
using Webignite.Core.Clients;

namespace Webignite.Core.Files;

/// <summary>A file or folder as the file explorer lists it.</summary>
public sealed record FileEntry(string Path, string Name, bool IsFolder, long? Size, DateTime Modified)
{
    public string Extension => IsFolder ? "" : System.IO.Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();
    public string Kind => FileKinds.Of(Name, IsFolder);
}

public enum FileView
{
    Thumbnails,
    List,
    Details,

    /// <summary>Finder's column view: each folder on the way in a column of its own, for deep folders.</summary>
    Columns,
}

public enum FileSort
{
    Name,
    Modified,
    Size,
    Kind,
}

public enum FileGrouping
{
    None,
    Kind,
    Date,
    Tag,
}

/// <summary>What a file is, from its name, in words a person uses.</summary>
public static class FileKinds
{
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "PDF",
        ["png"] = "Picture", ["jpg"] = "Picture", ["jpeg"] = "Picture", ["gif"] = "Picture", ["webp"] = "Picture",
        ["bmp"] = "Picture", ["svg"] = "Picture", ["ico"] = "Picture", ["tif"] = "Picture", ["tiff"] = "Picture", ["heic"] = "Picture",
        ["doc"] = "Word document", ["docx"] = "Word document", ["odt"] = "Word document", ["rtf"] = "Word document",
        ["xls"] = "Spreadsheet", ["xlsx"] = "Spreadsheet", ["csv"] = "Spreadsheet", ["ods"] = "Spreadsheet",
        ["ppt"] = "Presentation", ["pptx"] = "Presentation",
        ["txt"] = "Text", ["md"] = "Text", ["log"] = "Text",
        ["zip"] = "Zip", ["rar"] = "Zip", ["7z"] = "Zip", ["gz"] = "Zip", ["tar"] = "Zip",
        ["mp4"] = "Video", ["mov"] = "Video", ["avi"] = "Video", ["mkv"] = "Video", ["webm"] = "Video",
        ["mp3"] = "Music", ["wav"] = "Music", ["m4a"] = "Music", ["flac"] = "Music",
        ["exe"] = "Program", ["msi"] = "Program", ["bat"] = "Program", ["cmd"] = "Program",
        ["html"] = "Web page", ["htm"] = "Web page", ["css"] = "Code", ["js"] = "Code", ["php"] = "Code", ["json"] = "Code",
        ["psd"] = "Design", ["ai"] = "Design", ["fig"] = "Design", ["sketch"] = "Design", ["xd"] = "Design",
        ["ttf"] = "Font", ["otf"] = "Font", ["woff"] = "Font", ["woff2"] = "Font",
        ["eml"] = "Mail", ["msg"] = "Mail",
        ["lnk"] = "Shortcut", ["url"] = "Shortcut",
    };

    public static string Of(string name, bool isFolder)
    {
        if (isFolder)
            return "Folder";
        var extension = System.IO.Path.GetExtension(name).TrimStart('.');
        if (extension.Length == 0)
            return "File";
        return Kinds.TryGetValue(extension, out var kind) ? kind : extension.ToUpperInvariant() + " file";
    }

    /// <summary>Pictures the app can show itself (thumbnails and the preview).</summary>
    public static bool IsPicture(string name) =>
        System.IO.Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";

    public static bool IsPdf(string name) => System.IO.Path.GetExtension(name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the preview can show inside the app.</summary>
    public static bool CanPreview(string name) => IsPicture(name) || IsPdf(name);
}

/// <summary>Putting a folder's contents in order and into groups.</summary>
public static class FileLists
{
    /// <summary>Folders first, then by the chosen column. Names sort as a person reads them: "File 2" before "File 10".</summary>
    public static IReadOnlyList<FileEntry> Sort(IEnumerable<FileEntry> entries, FileSort by, bool descending)
    {
        IEnumerable<FileEntry> sorted = by switch
        {
            FileSort.Modified => entries.OrderBy(e => e.Modified).ThenBy(e => e.Name, NaturalOrder.Instance),
            FileSort.Size => entries.OrderBy(e => e.Size ?? -1).ThenBy(e => e.Name, NaturalOrder.Instance),
            FileSort.Kind => entries.OrderBy(e => e.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, NaturalOrder.Instance),
            _ => entries.OrderBy(e => e.Name, NaturalOrder.Instance),
        };
        var list = sorted.ToList();
        if (descending)
            list.Reverse();
        return [.. list.Where(e => e.IsFolder), .. list.Where(e => !e.IsFolder)];
    }

    /// <summary>The group a file falls in, as its heading reads.</summary>
    public const string NoTag = "No tag";

    /// <summary>The group a file falls in, as its heading reads. By tag, a file with several tags goes under the first in the tag list.</summary>
    public static string GroupOf(FileEntry entry, FileGrouping by, DateOnly today, Func<FileEntry, string?>? firstTag = null) => by switch
    {
        FileGrouping.Kind => entry.IsFolder ? "Folders" : entry.Kind,
        FileGrouping.Date => DateGroup(DateOnly.FromDateTime(entry.Modified), today),
        FileGrouping.Tag => firstTag?.Invoke(entry) ?? NoTag,
        _ => "",
    };

    /// <summary>
    /// The groups in the order they show: newest dates first, kinds A to Z with folders on top, tags in the order
    /// of the tag list with untagged files last.
    /// </summary>
    public static IReadOnlyList<(string Heading, IReadOnlyList<FileEntry> Entries)> Group(IReadOnlyList<FileEntry> sorted, FileGrouping by, DateOnly today,
        Func<FileEntry, string?>? firstTag = null, IReadOnlyList<string>? tagOrder = null)
    {
        if (by == FileGrouping.None)
            return [("", sorted)];
        var groups = sorted.GroupBy(e => GroupOf(e, by, today, firstTag)).Select(g => (g.Key, (IReadOnlyList<FileEntry>)[.. g]));
        return by switch
        {
            FileGrouping.Date => [.. groups.OrderBy(g => Array.IndexOf(DateGroups, g.Key))],
            FileGrouping.Tag => [.. groups.OrderBy(g => g.Key == NoTag ? int.MaxValue : TagPlace(tagOrder, g.Key))],
            _ => [.. groups.OrderBy(g => g.Key == "Folders" ? 0 : 1).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)],
        };
    }

    private static int TagPlace(IReadOnlyList<string>? order, string tag)
    {
        var place = order is null ? -1 : order.ToList().IndexOf(tag);
        return place < 0 ? int.MaxValue - 1 : place;
    }

    private static readonly string[] DateGroups = ["Today", "Yesterday", "Earlier this week", "Earlier this month", "Earlier this year", "Older"];

    private static string DateGroup(DateOnly day, DateOnly today)
    {
        if (day >= today)
            return "Today";
        if (day == today.AddDays(-1))
            return "Yesterday";
        if (day >= Dates.WeekStart(today))
            return "Earlier this week";
        if (day.Year == today.Year && day.Month == today.Month)
            return "Earlier this month";
        return day.Year == today.Year ? "Earlier this year" : "Older";
    }
}

/// <summary>Compares names the way people read them: digits as numbers, case ignored.</summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static NaturalOrder Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        x ??= "";
        y ??= "";
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var startI = i;
                var startJ = j;
                while (i < x.Length && char.IsDigit(x[i]))
                    i++;
                while (j < y.Length && char.IsDigit(y[j]))
                    j++;
                var a = x[startI..i].TrimStart('0');
                var b = y[startJ..j].TrimStart('0');
                var byLength = a.Length.CompareTo(b.Length);
                if (byLength != 0)
                    return byLength;
                var byDigits = string.CompareOrdinal(a, b);
                if (byDigits != 0)
                    return byDigits;
                continue;
            }
            var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
            if (c != 0)
                return c;
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}

/// <summary>What a file or folder may be called, and names for new ones and copies.</summary>
public static class FileNames
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why a name can't be used in Windows, or null when it can.</summary>
    public static string? Problem(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return "Type a name.";
        if (trimmed.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            return "A name can't have any of these in it: \\ / : * ? \" < > |";
        if (trimmed.EndsWith('.'))
            return "A name can't end with a full stop.";
        if (Reserved.Contains(System.IO.Path.GetFileNameWithoutExtension(trimmed)))
            return $"Windows keeps the name {trimmed} for itself.";
        if (trimmed.Length > 240)
            return "That name is too long.";
        return null;
    }

    /// <summary>A name made safe for a folder: "Smith & Co. (Pty) Ltd." becomes "Smith & Co. (Pty) Ltd".</summary>
    public static string Safe(string? name)
    {
        var cleaned = new string([.. (name ?? "").Select(c => c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c) ? ' ' : c)]);
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', ' ');
        if (cleaned.Length > 120)
            cleaned = cleaned[..120].TrimEnd('.', ' ');
        return cleaned.Length == 0 || Reserved.Contains(cleaned) ? "Client" : cleaned;
    }

    /// <summary>"New folder", or "New folder (2)" and on when that is taken.</summary>
    public static string NewFolder(IEnumerable<string> existing) => Free("New folder", "", existing);

    /// <summary>The name a copy gets in the same folder: "Brief - Copy.pdf", then "Brief - Copy (2).pdf".</summary>
    public static string CopyOf(string name, bool isFolder, IEnumerable<string> existing)
    {
        var stem = isFolder ? name : System.IO.Path.GetFileNameWithoutExtension(name);
        var extension = isFolder ? "" : System.IO.Path.GetExtension(name);
        return Free(stem + " - Copy", extension, existing);
    }

    /// <summary>The name itself when free, else with (2), (3) and on: for a file brought in beside one of the same name.</summary>
    public static string Unique(string name, bool isFolder, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
            return name;
        var stem = isFolder ? name : System.IO.Path.GetFileNameWithoutExtension(name);
        var extension = isFolder ? "" : System.IO.Path.GetExtension(name);
        return Free(stem, extension, taken, startAt: 2);
    }

    private static string Free(string stem, string extension, IEnumerable<string> existing, int startAt = 1)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = startAt; ; n++)
        {
            var candidate = n == 1 ? stem + extension : $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}){extension}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}

/// <summary>A folder path as the address bar shows it: each folder on the way, clickable.</summary>
public static class FilePaths
{
    /// <summary>The folders from the drive down: ("C:", "C:\"), ("Projects", "C:\Projects") and so on.</summary>
    public static IReadOnlyList<(string Name, string Path)> Crumbs(string path)
    {
        var crumbs = new List<(string, string)>();
        var current = path.TrimEnd('\\', '/');
        if (current.Length == 0)
            current = path;
        while (!string.IsNullOrEmpty(current))
        {
            var parent = Parent(current);
            var name = parent is null ? current.TrimEnd('\\', '/') : current[(current.LastIndexOfAny(['\\', '/']) + 1)..];
            crumbs.Add((name.Length == 0 ? current : name, parent is null && current.Length <= 3 && current.EndsWith(':') ? current + "\\" : current));
            current = parent ?? "";
        }
        crumbs.Reverse();
        return crumbs;
    }

    /// <summary>The folder a path is in, or null at the top of a drive.</summary>
    public static string? Parent(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        if (cut < 0)
            return null;
        if (cut == 0)
            return trimmed.Length > 1 ? trimmed[..1] : null; // "/home" → "/"
        var parent = trimmed[..cut];
        return parent.EndsWith(':') ? parent + "\\" : parent;
    }
}

/// <summary>A file's size the way the details view shows it.</summary>
public static class FileSizes
{
    public static string Format(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{Math.Ceiling(bytes.Value / 1024.0).ToString("0", CultureInfo.CurrentCulture)} KB",
        < 1024L * 1024 * 1024 => $"{(bytes.Value / 1024.0 / 1024.0).ToString("0.0", CultureInfo.CurrentCulture)} MB",
        _ => $"{(bytes.Value / 1024.0 / 1024.0 / 1024.0).ToString("0.0", CultureInfo.CurrentCulture)} GB",
    };
}

/// <summary>Which folder holds each client's work, found by name in the main client folder (e.g. C:\Projects).</summary>
public static class ClientFolders
{
    public enum Fit
    {
        /// <summary>The folder is the client's: picked by hand, or the name is the same.</summary>
        Sure,

        /// <summary>A folder looks like it, but the names differ: ask before using it.</summary>
        Likely,

        None,
    }

    public sealed record Match(Fit Fit, string? Folder);

    /// <summary>
    /// The folder for a client: the one picked by hand if it is set; else a folder named as the company or the
    /// client; else the one folder whose name starts the same or shares the first word, offered to confirm.
    /// </summary>
    public static Match For(Client client, IEnumerable<string> folders)
    {
        if (!string.IsNullOrWhiteSpace(client.Folder))
            return new Match(Fit.Sure, client.Folder);
        return Find([client.Company, client.Name], folders);
    }

    /// <summary>The folder among these that goes by one of the names, the first name counting most.</summary>
    public static Match Find(IEnumerable<string?> names, IEnumerable<string> folders)
    {
        var paths = folders.ToList();
        var keys = names.Select(Key).Where(k => k.Length > 0).Distinct().ToList();
        foreach (var key in keys)
        {
            if (paths.FirstOrDefault(p => Key(Name(p)) == key) is { } same)
                return new Match(Fit.Sure, same);
        }
        foreach (var key in keys)
        {
            var first = key.Split(' ')[0];
            var likely = paths.Where(p =>
            {
                var folder = Key(Name(p));
                return folder.Length > 0 && (folder.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(folder, StringComparison.Ordinal)
                    || (first.Length >= 4 && folder.Split(' ')[0] == first));
            }).ToList();
            if (likely.Count == 1)
                return new Match(Fit.Likely, likely[0]);
        }
        return new Match(Fit.None, null);
    }

    // Names compared the way clients are, ignoring "(Pty) Ltd", "cc" and punctuation.
    private static string Key(string? name)
    {
        var words = ClientDirectory.Key(new string([.. (name ?? "").Select(c => char.IsLetterOrDigit(c) ? c : ' ')]))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("pty" or "ltd" or "cc" or "inc" or "llc" or "the"));
        return string.Join(' ', words);
    }

    internal static string Name(string path) => path.TrimEnd('\\', '/')[(path.TrimEnd('\\', '/').LastIndexOfAny(['\\', '/']) + 1)..];
}

/// <summary>
/// Folders the app offers to make for a person who wants their work filed for them: one per client, named after
/// the company, and one per project inside it. Clients we work with directly go together under one folder
/// (e.g. Clients\Webignite\Trimax); a studio's own folder sits at the top with its projects inside (Clients\G3\Amos).
/// Nothing here touches the disk: these only say where things go.
/// </summary>
public static class WorkFolders
{
    /// <summary>What a client's folder is called.</summary>
    public static string NameFor(Client client) => FileNames.Safe(client.Called);

    /// <summary>Where a client's folder goes under the main client folder.</summary>
    public static string ClientPath(Client client, string root, FileSettings settings)
    {
        var direct = (settings.DirectFolder ?? "").Trim();
        return client.Direct && direct.Length > 0
            ? System.IO.Path.Combine(root, FileNames.Safe(direct), NameFor(client))
            : System.IO.Path.Combine(root, NameFor(client));
    }

    /// <summary>The folder inside the main one that direct clients share, or null when they aren't kept apart.</summary>
    public static string? DirectPath(string root, FileSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DirectFolder) ? null : System.IO.Path.Combine(root, FileNames.Safe(settings.DirectFolder));

    /// <summary>Where a project's folder goes inside its client's.</summary>
    public static string ProjectPath(string clientFolder, string projectName) =>
        System.IO.Path.Combine(clientFolder, FileNames.Safe(string.IsNullOrWhiteSpace(projectName) ? "Project" : projectName));

    /// <summary>
    /// A project's folder: the one linked on this PC if it is set; else a folder in the client's with the
    /// project's name, or the one that looks like it ("Amos" for "Amos website"), offered to confirm.
    /// </summary>
    public static ClientFolders.Match ForProject(string projectId, string projectName, FileSettings settings, IEnumerable<string> inClientFolder) =>
        settings.ProjectFolders.TryGetValue(projectId, out var linked) && !string.IsNullOrWhiteSpace(linked)
            ? new ClientFolders.Match(ClientFolders.Fit.Sure, linked)
            : ClientFolders.Find([projectName], inClientFolder);

    /// <summary>
    /// After a rename: where the folder would move to so it keeps the new name, or null when it should be left
    /// alone (it never had the old name, or the name on disk wouldn't change).
    /// </summary>
    public static string? Renamed(string folder, string oldName, string newName)
    {
        var was = FileNames.Safe(oldName);
        var now = FileNames.Safe(newName);
        if (string.Equals(was, now, StringComparison.OrdinalIgnoreCase) || !string.Equals(ClientFolders.Name(folder), was, StringComparison.OrdinalIgnoreCase))
            return null;
        return FilePaths.Parent(folder) is { } parent ? System.IO.Path.Combine(parent, now) : null;
    }

    /// <summary>The settings with a folder linked to a project, or the link taken away with null.</summary>
    public static FileSettings Link(FileSettings settings, string projectId, string? folder)
    {
        var links = settings.ProjectFolders.ToDictionary(p => p.Key, p => p.Value);
        if (string.IsNullOrWhiteSpace(folder))
            links.Remove(projectId);
        else
            links[projectId] = folder;
        return settings with { ProjectFolders = links };
    }

    /// <summary>The settings after a folder moved: project folders inside it follow.</summary>
    public static FileSettings Moved(FileSettings settings, string from, string to)
    {
        var inside = from.TrimEnd('\\', '/');
        string Follow(string path) =>
            string.Equals(path, inside, StringComparison.OrdinalIgnoreCase) ? to
            : path.StartsWith(inside + "\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(inside + "/", StringComparison.OrdinalIgnoreCase) ? to + path[inside.Length..]
            : path;
        return settings with { ProjectFolders = settings.ProjectFolders.ToDictionary(p => p.Key, p => Follow(p.Value)) };
    }

    /// <summary>A path as a note shows it: from the main client folder down, e.g. "Clients\G3\Amos".</summary>
    public static string Short(string path, string? root)
    {
        if (root is null || FilePaths.Parent(root) is not { } above || !path.StartsWith(above, StringComparison.OrdinalIgnoreCase))
            return path;
        return path[above.Length..].TrimStart('\\', '/');
    }
}

/// <summary>Where the file explorer looks for client folders.</summary>
public sealed record FileSettings
{
    /// <summary>The folder client folders live in, e.g. C:\Projects. Empty until chosen (C:\Projects is tried first).</summary>
    public string? ClientRoot { get; init; }

    public FileView View { get; init; } = FileView.Details;
    public FileSort Sort { get; init; } = FileSort.Name;
    public bool Descending { get; init; }
    public FileGrouping Grouping { get; init; }

    /// <summary>The thumbnails' size, in pixels.</summary>
    public int ThumbnailSize { get; init; } = 120;

    /// <summary>Folders pinned to the side list, in the order they were added.</summary>
    public IReadOnlyList<string> Favourites { get; init; } = [];

    /// <summary>Whether the details pane (preview, size, dates) shows beside the list.</summary>
    public bool ShowDetails { get; init; }

    /// <summary>The folder files were last picked from or saved in, where the next pick starts.</summary>
    public string? LastPicked { get; init; }

    /// <summary>
    /// Offer to make a folder when a client is added and when a project gets its client. Kept on this PC only,
    /// like the rest of these, so each of the two chooses for themselves. Off until switched on.
    /// </summary>
    public bool OfferFolders { get; init; }

    /// <summary>The folder inside the main one for clients we work with directly; empty puts them at the top with the rest.</summary>
    public string? DirectFolder { get; init; } = "Webignite";

    /// <summary>A folder whose contents every new project folder starts with, e.g. C:\Projects\Clients\_TEMPLATE-new-site.</summary>
    public string? ProjectTemplate { get; init; }

    /// <summary>Each project's folder on this PC, by the project's id.</summary>
    public IReadOnlyDictionary<string, string> ProjectFolders { get; init; } = new Dictionary<string, string>();
}
