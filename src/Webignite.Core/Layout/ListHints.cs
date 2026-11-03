namespace Webignite.Core.Layout;

/// <summary>
/// When a list is short, the space under it carries one line: a hint or the next useful thing to do.
/// Long lists, filtered lists and empty lists don't get one (empty lists have their own empty state).
/// </summary>
public static class ListHints
{
    /// <summary>Lists with fewer rows than this count as short.</summary>
    public const int ShortList = 6;

    public static bool Shows(int rows, bool filtered) => !filtered && rows is > 0 and < ShortList;
}
