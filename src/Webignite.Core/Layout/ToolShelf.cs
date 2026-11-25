namespace Webignite.Core.Layout;

/// <summary>
/// The Tools page: every small tool as a tile, under tabs, with a box to find one by name or by what it does.
/// A tool pinned there shows on the menus too.
/// </summary>
public static class ToolShelf
{
    /// <summary>
    /// Whether a tool fits what was typed: every word typed is somewhere in its name or its line of what it does,
    /// in any order and any case. Nothing typed fits everything.
    /// </summary>
    public static bool Matches(string? typed, string title, string about)
    {
        var words = (typed ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.All(w => title.Contains(w, StringComparison.OrdinalIgnoreCase) || about.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}
