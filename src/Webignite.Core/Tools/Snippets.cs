namespace Webignite.Core.Tools;

/// <summary>
/// A saved block of text to paste anywhere: banking details, a standard reply, a piece of code. The code is a
/// short word to find it by ("bank"); it is optional.
/// </summary>
public sealed record Snippet(string Id, string Name, string Code, string Text);

/// <summary>The person's snippets, in the order they keep them.</summary>
public sealed record SnippetSettings
{
    public IReadOnlyList<Snippet> Items { get; init; } = [];
}

public static class Snippets
{
    /// <summary>The longest a snippet may be. Far more than a block of text needs; it is kept in settings.</summary>
    public const int Longest = 20_000;

    /// <summary>
    /// The snippets that fit what was typed, best first: the one whose code it is, then codes and names that
    /// start with it, then anything with it in the name, code or text. Nothing typed gives them all.
    /// </summary>
    public static IReadOnlyList<Snippet> Find(IReadOnlyList<Snippet> items, string? typed)
    {
        var search = (typed ?? "").Trim();
        if (search.Length == 0)
            return items;
        return [.. items
            .Select((snippet, index) => (snippet, index, rank: Rank(snippet, search)))
            .Where(found => found.rank < 4)
            .OrderBy(found => found.rank).ThenBy(found => found.index)
            .Select(found => found.snippet)];
    }

    private static int Rank(Snippet snippet, string search)
    {
        const StringComparison any = StringComparison.OrdinalIgnoreCase;
        if (snippet.Code.Length > 0 && snippet.Code.Equals(search, any))
            return 0;
        if ((snippet.Code.Length > 0 && snippet.Code.StartsWith(search, any)) || snippet.Name.StartsWith(search, any))
            return 1;
        if (snippet.Name.Contains(search, any) || snippet.Code.Contains(search, any))
            return 2;
        return snippet.Text.Contains(search, any) ? 3 : 4;
    }

    /// <summary>
    /// The text as it is pasted: {date} becomes today's date, {time} the time, and {clipboard} what was on the
    /// clipboard, so a reply can be wrapped round something just copied.
    /// </summary>
    public static string Fill(string text, string date, string time, string? clipboard) =>
        text.Replace("{date}", date, StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", time, StringComparison.OrdinalIgnoreCase)
            .Replace("{clipboard}", clipboard ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Why a snippet can't be saved as typed, or null when it can.</summary>
    public static string? Problem(IReadOnlyList<Snippet> items, string? id, string? name, string? code, string? text)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Give it a name to find it by.";
        if (string.IsNullOrWhiteSpace(text))
            return "There is no text to paste yet.";
        if (text.Length > Longest)
            return $"That is too long for a snippet. Keep it under {Longest:N0} characters.";
        var short_ = (code ?? "").Trim();
        if (short_.Any(char.IsWhiteSpace))
            return "A code is one word, with no spaces.";
        if (short_.Length > 0 && items.Any(other => other.Id != id && other.Code.Equals(short_, StringComparison.OrdinalIgnoreCase)))
            return $"Another snippet has the code \"{short_}\" already.";
        return null;
    }

    /// <summary>The list with a snippet added, or put in place of the one with its id.</summary>
    public static IReadOnlyList<Snippet> Save(IReadOnlyList<Snippet> items, Snippet snippet)
    {
        var tidy = snippet with { Name = snippet.Name.Trim(), Code = snippet.Code.Trim() };
        return items.Any(other => other.Id == tidy.Id) ? [.. items.Select(other => other.Id == tidy.Id ? tidy : other)] : [.. items, tidy];
    }

    public static IReadOnlyList<Snippet> Remove(IReadOnlyList<Snippet> items, string id) => [.. items.Where(other => other.Id != id)];

    /// <summary>The first line, cut short, to tell snippets apart down a list.</summary>
    public static string Preview(string text)
    {
        var line = text.Trim().Split('\n', 2)[0].Trim();
        var more = text.Trim().Contains('\n', StringComparison.Ordinal);
        return line.Length > 70 ? line[..70].TrimEnd() + "…" : more ? line + " …" : line;
    }
}
