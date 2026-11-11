namespace Webignite.Core.Chat;

/// <summary>A message between the two of you: who wrote it, the words, any files, and when.</summary>
public sealed record ChatMessage
{
    public required string Id { get; init; }

    /// <summary>The writer's person id (the team's ids, the same on both PCs).</summary>
    public required string From { get; init; }

    public string Text { get; init; } = "";
    public IReadOnlyList<ChatFile> Files { get; init; } = [];
    public required DateTimeOffset Sent { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>A file or screenshot sent with a message. Its bytes travel in parts (<see cref="ChatPart"/>).</summary>
public sealed record ChatFile(string Id, string Name, long Bytes, int Parts);

/// <summary>One piece of a file, small enough to travel as one record.</summary>
public sealed record ChatPart(string Id, string FileId, int Index, byte[] Data);

/// <summary>What a paste into the chat box adds: files copied in Explorer, a picture, or words.</summary>
public enum ChatPaste { Nothing, Files, Picture, Text }

public static class ChatRules
{
    /// <summary>
    /// What a paste adds, as WhatsApp does: files copied in Explorer go with the message; a picture (a screenshot,
    /// or one copied from a web page, which brings its address along as words) goes as a picture; anything else
    /// with words, such as a few lines from Word, pastes as words.
    /// </summary>
    public static ChatPaste PasteAs(bool hasFiles, bool hasPicture, string? text)
    {
        if (hasFiles)
            return ChatPaste.Files;
        var words = text?.Trim() ?? "";
        if (hasPicture && (words.Length == 0 || IsAddress(words)))
            return ChatPaste.Picture;
        return text is { Length: > 0 } ? ChatPaste.Text : ChatPaste.Nothing;
    }

    // One web address or file path on its own, as a browser puts beside a copied picture.
    private static bool IsAddress(string words) =>
        !words.Any(char.IsWhiteSpace)
        && (words.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || words.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || words.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || words.StartsWith("file:", StringComparison.OrdinalIgnoreCase));

    /// <summary>The biggest file that can be sent: enough for screenshots and documents, small enough for the server.</summary>
    public const long MostBytes = 10 * 1024 * 1024;

    /// <summary>Each part, so a locked part stays well under the server's limit for one record.</summary>
    public const int PartBytes = 256 * 1024;

    private static readonly HashSet<string> PictureTypes = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    public static bool IsPicture(string name) => PictureTypes.Contains(Path.GetExtension(name));

    /// <summary>Null when the file can be sent, else why not.</summary>
    public static string? Problem(string name, long bytes) => bytes switch
    {
        0 => $"{name} is empty.",
        > MostBytes => $"{name} is bigger than 10 MB. Share it another way, such as a link.",
        _ => null,
    };

    /// <summary>A file cut into parts, with the record that names it in a message.</summary>
    public static (ChatFile File, IReadOnlyList<ChatPart> Parts) Split(string name, byte[] data)
    {
        var fileId = Guid.CreateVersion7().ToString("N");
        var parts = new List<ChatPart>();
        for (var at = 0; at < data.Length; at += PartBytes)
            parts.Add(new ChatPart($"{fileId}-{parts.Count}", fileId, parts.Count, data[at..Math.Min(data.Length, at + PartBytes)]));
        return (new ChatFile(fileId, name, data.Length, parts.Count), parts);
    }

    /// <summary>The file's bytes again, or null while some parts haven't arrived yet.</summary>
    public static byte[]? Join(ChatFile file, IEnumerable<ChatPart> parts)
    {
        var mine = parts.Where(p => p.FileId == file.Id).DistinctBy(p => p.Index).OrderBy(p => p.Index).ToList();
        if (mine.Count != file.Parts || mine.Select(p => p.Index).Where((index, i) => index != i).Any())
            return null;
        var data = mine.SelectMany(p => p.Data).ToArray();
        return data.Length == file.Bytes ? data : null;
    }

    /// <summary>Messages from the other person since <paramref name="readUpTo"/>.</summary>
    public static int Unread(IEnumerable<ChatMessage> messages, string me, DateTimeOffset readUpTo) =>
        messages.Count(m => m.From != me && m.Sent > readUpTo);

    /// <summary>When a message was sent: "Today at 10:15", "Yesterday at 16:02", "4 Nov at 09:30", "4 Nov 2025 at 09:30".</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone, IFormatProvider culture)
    {
        var said = Projects.TaskTalk.When(at, now, zone, culture);
        return said.Contains(" at ", StringComparison.Ordinal) ? said : said + " at " + TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", culture);
    }

    /// <summary>What a note about a new message says: the words, cut short, or what was sent.</summary>
    public static string Preview(ChatMessage message)
    {
        var text = message.Text.Trim().ReplaceLineEndings(" ");
        if (text.Length > 90)
            text = text[..87] + "…";
        return text.Length > 0 ? text
            : message.Files.Count == 1 ? (IsPicture(message.Files[0].Name) ? "Sent a picture." : $"Sent {message.Files[0].Name}.")
            : $"Sent {message.Files.Count} files.";
    }
}
