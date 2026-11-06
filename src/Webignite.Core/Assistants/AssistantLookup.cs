using System.Globalization;

namespace Webignite.Core.Assistants;

/// <summary>
/// How Claude and ChatGPT name things in the app: by id, or by name as Eddie would say it. A name that fits exactly
/// wins; otherwise one that holds the words, when only one does. Anything else comes back as a problem naming the
/// close ones, so the assistant can ask or pick.
/// </summary>
public static class AssistantLookup
{
    public static (T? Found, string Problem) Find<T>(IEnumerable<T> items, string? typed, Func<T, string> id, Func<T, string> name, string what)
        where T : class
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0)
            return (null, $"Say which {what}.");
        var list = items.ToList();
        if (list.FirstOrDefault(i => id(i) == text) is { } byId)
            return (byId, "");
        var exact = list.Where(i => string.Equals(name(i).Trim(), text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return (exact[0], "");
        var holding = exact.Count > 1 ? exact : [.. list.Where(i => name(i).Contains(text, StringComparison.OrdinalIgnoreCase))];
        return holding.Count switch
        {
            1 => (holding[0], ""),
            0 => (null, $"No {what} called \"{text}\"."),
            _ => (null, $"More than one {what} fits \"{text}\": {string.Join(", ", holding.Take(5).Select(i => $"{name(i)} ({id(i)})"))}. Use the id."),
        };
    }

    /// <summary>A date as assistants send it (yyyy-MM-dd); "none" or "clear" takes it off. Null when it's left out.</summary>
    public static (bool Given, DateOnly? Date, string Problem) Date(string? typed, string what)
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0)
            return (false, null, "");
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) || text.Equals("clear", StringComparison.OrdinalIgnoreCase))
            return (true, null, "");
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? (true, date, "")
            : (false, null, $"Give {what} as a date like 2026-11-14, or none to take it off.");
    }

    public static string? Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
