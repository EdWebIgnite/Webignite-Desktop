using System.Globalization;

namespace Webignite.Core.Tools;

/// <summary>
/// The last things copied, newest first, kept in memory while Webignite is open and never written to disk.
/// Copies marked as private (passwords, from Webignite's site logins or a password manager) never get here.
/// </summary>
public static class ClipboardHistory
{
    public const int Most = 25;

    /// <summary>Text longer than this isn't kept: a whole document copied isn't what the history is for.</summary>
    public const int LongestKept = 100_000;

    /// <summary>The list with this copy at the top. Copying something again moves it up instead of adding it twice.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> items, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > LongestKept)
            return items;
        return [text, .. items.Where(i => i != text).Take(Most - 1)];
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> items, string text) => [.. items.Where(i => i != text)];

    /// <summary>One line to show in the list: spaces and line breaks squashed, cut short with an ellipsis.</summary>
    public static string Preview(string text, int length = 90)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= length ? line : line[..(length - 1)].TrimEnd() + "…";
    }

    /// <summary>"3 lines, 240 characters", shown under a long copy.</summary>
    public static string Size(string text)
    {
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Length;
        var characters = text.Length.ToString("N0", CultureInfo.CurrentCulture);
        return lines > 1 ? $"{lines} lines, {characters} characters" : $"{characters} characters";
    }
}
