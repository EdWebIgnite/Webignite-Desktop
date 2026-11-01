using System.Globalization;

namespace Webignite.Core.Clients;

public sealed record ClientNote
{
    public required string Id { get; init; }
    public required DateOnly Date { get; init; }
    public required string Text { get; init; }
}

/// <summary>A link to a file on this PC. The file itself stays where it is.</summary>
public sealed record ClientFile
{
    public required string Path { get; init; }
    public required DateOnly Added { get; init; }

    // Split on both slashes so a Windows path reads right wherever it's shown.
    public string Name => Path[(Path.LastIndexOfAny(['\\', '/']) + 1)..];
}

/// <summary>Keeping a client's notes and files in order.</summary>
public static class ClientRecords
{
    public const int MaxNote = 4000;

    public static Client AddNote(Client client, string text, DateOnly date)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
            return client;
        if (text.Length > MaxNote)
            throw new ArgumentException($"Keep a note under {MaxNote} characters, or attach the full write-up as a file.");
        var note = new ClientNote { Id = Guid.CreateVersion7().ToString("N"), Date = date, Text = text };
        return client with { Log = [note, .. client.Log.OrderByDescending(n => n.Date)] };
    }

    public static Client RemoveNote(Client client, string id) => client with { Log = client.Log.Where(n => n.Id != id).ToList() };

    /// <summary>Adds files, skipping any already linked (same path, ignoring case). Newest first, then by name.</summary>
    public static Client AddFiles(Client client, IEnumerable<string> paths, DateOnly today)
    {
        var known = new HashSet<string>(client.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var added = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
            .Where(known.Add)
            .Select(p => new ClientFile { Path = p, Added = today });
        return client with { Files = [.. added.Concat(client.Files).OrderByDescending(f => f.Added).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)] };
    }

    public static Client RemoveFile(Client client, string path) =>
        client with { Files = client.Files.Where(f => !string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)).ToList() };

    /// <summary>A short label for the kind of file, for the list.</summary>
    public static string Kind(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "PDF",
        ".doc" or ".docx" or ".odt" or ".rtf" => "Document",
        ".xls" or ".xlsx" or ".csv" or ".ods" => "Spreadsheet",
        ".ppt" or ".pptx" => "Slides",
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".svg" or ".ai" or ".psd" or ".eps" => "Image",
        ".zip" or ".rar" or ".7z" => "Archive",
        ".txt" or ".md" => "Text",
        _ => "File",
    };

    public static string Day(DateOnly date, DateOnly today) =>
        date == today ? "Today" : date == today.AddDays(-1) ? "Yesterday"
        : date.Year == today.Year ? date.ToString("d MMM", CultureInfo.InvariantCulture) : date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
