namespace Webignite.Core.Layout;

/// <summary>
/// A list that can grow long shows its first few rows until the person asks for the rest. A row on screen is not
/// cheap (its parts and their styles come to about 120 KB), so a day with 200 things on it must not draw all 200
/// just because the page was opened.
/// </summary>
public static class FirstFew
{
    /// <summary>The rows to draw: the first few, or everything once asked for.</summary>
    public static IEnumerable<T> Of<T>(IReadOnlyList<T> all, int first, bool showAll) => showAll ? all : all.Take(first);

    /// <summary>The words under the list: "Show 7 more", or "Show fewer" once everything is showing; empty when it all fits.</summary>
    public static string MoreText(int total, int first, bool showAll) =>
        total <= first ? "" : showAll ? "Show fewer" : $"Show {total - first} more";

    /// <summary>How many rows are left out, for a list that sends the person to the full page instead of growing.</summary>
    public static int LeftOut(int total, int first) => Math.Max(0, total - first);
}
