using Webignite.Core.Mail;

namespace Webignite.Core.Files;

/// <summary>A tag you make for files and folders, such as "Final" in blue, as in Apple's Finder.</summary>
/// <param name="Id">Stays the same when the tag is renamed or recoloured, so tagged files keep it.</param>
/// <param name="Name">What you see.</param>
/// <param name="Colour">One of the Mail category colours (<see cref="MailCategories.Colours"/>).</param>
public sealed record FileTag(string Id, string Name, string Colour);

/// <summary>
/// A file or folder with tags. Kept in the app, not on the file, so Windows Explorer doesn't show them. The file's
/// Windows id (its volume and file number) is kept too, so the tags find it again after it was renamed or moved on
/// the same drive outside the app.
/// </summary>
public sealed record TaggedFile
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The file's id on its drive, e.g. "9a3c11f0-0001000000004c2e", when Windows gave one.</summary>
    public string? FileKey { get; init; }
}

/// <summary>The tag list, and which files carry which tags.</summary>
public static class FileTags
{
    public const int LongestName = 30;

    /// <summary>The tags a new install starts with. They can be renamed, recoloured or removed.</summary>
    public static IReadOnlyList<FileTag> Defaults { get; } =
    [
        new("final", "Final", "blue"),
        new("needs-ok", "Needs client OK", "orange"),
        new("invoice-sent", "Invoice sent", "purple"),
    ];

    /// <summary>What is wrong with this name, or null when it can be used.</summary>
    public static string? Check(IReadOnlyList<FileTag> existing, string name, string? editing = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "Give the tag a name.";
        if (trimmed.Length > LongestName)
            return $"Keep the name under {LongestName} letters.";
        if (existing.Any(t => t.Id != editing && t.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return "There is already a tag with that name.";
        return null;
    }

    /// <summary>Adds a tag. Without a colour it gets the first one not used yet.</summary>
    public static IReadOnlyList<FileTag> Add(IReadOnlyList<FileTag> existing, string name, string? colour = null)
    {
        if (Check(existing, name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        var trimmed = name.Trim();
        var stem = new string([.. trimmed.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-');
        if (stem.Length == 0)
            stem = "tag";
        var id = stem;
        for (var n = 2; existing.Any(t => t.Id == id); n++)
            id = $"{stem}-{n}";
        var used = existing.Select(t => t.Colour).ToHashSet();
        var pick = colour is not null && MailCategories.Colours.Contains(colour) ? colour
            : MailCategories.SuggestedOrder.FirstOrDefault(c => !used.Contains(c)) ?? MailCategories.SuggestedOrder[existing.Count % MailCategories.SuggestedOrder.Count];
        return [.. existing, new FileTag(id, trimmed, pick)];
    }

    /// <summary>Renames or recolours a tag. Files keep it, because its id doesn't change.</summary>
    public static IReadOnlyList<FileTag> Change(IReadOnlyList<FileTag> existing, string id, string name, string colour)
    {
        if (Check(existing, name, id) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        return [.. existing.Select(t => t.Id == id ? t with { Name = name.Trim(), Colour = MailCategories.Colours.Contains(colour) ? colour : t.Colour } : t)];
    }

    public static IReadOnlyList<FileTag> Remove(IReadOnlyList<FileTag> existing, string id) => [.. existing.Where(t => t.Id != id)];

    /// <summary>The file with a tag put on or taken off; null when it has no tags left, so its record can go.</summary>
    public static TaggedFile? With(TaggedFile? file, string path, string tagId, bool on, Func<string> newId)
    {
        var tags = file?.Tags ?? [];
        tags = on ? (tags.Contains(tagId) ? tags : [.. tags, tagId]) : [.. tags.Where(t => t != tagId)];
        if (tags.Count == 0)
            return null;
        return file is null ? new TaggedFile { Id = newId(), Path = path, Tags = tags } : file with { Tags = tags };
    }

    /// <summary>
    /// Where tagged files went when a file or folder moved from one path to another: the item itself, and for a
    /// folder everything inside it. Only the files that changed are returned.
    /// </summary>
    public static IReadOnlyList<TaggedFile> Moved(IEnumerable<TaggedFile> files, string from, string to)
    {
        var old = Trim(from);
        var moved = new List<TaggedFile>();
        foreach (var file in files)
        {
            var path = Trim(file.Path);
            if (Same(path, old))
                moved.Add(file with { Path = Trim(to) });
            else if (path.Length > old.Length && path.StartsWith(old, StringComparison.OrdinalIgnoreCase) && path[old.Length] is '\\' or '/')
                moved.Add(file with { Path = Trim(to) + path[old.Length..] });
        }
        return moved;
    }

    /// <summary>The tags a file has, in the order of the tag list. Tags since removed are left out.</summary>
    public static IReadOnlyList<FileTag> Of(IReadOnlyList<FileTag> all, TaggedFile? file) =>
        file is null || file.Tags.Count == 0 ? [] : [.. all.Where(t => file.Tags.Contains(t.Id))];

    public static bool Same(string a, string b) => string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The key a path is looked up by: letter case and a trailing slash don't matter, as on Windows.</summary>
    public static string Key(string path) => Trim(path).ToUpperInvariant();

    private static string Trim(string path) => path.Length > 3 ? path.TrimEnd('\\', '/') : path;
}
