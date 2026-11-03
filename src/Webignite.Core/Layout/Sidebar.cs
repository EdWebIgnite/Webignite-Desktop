namespace Webignite.Core.Layout;

/// <summary>
/// How the sidebar was left: tucked away or showing, which groups of pages are folded up, and the order
/// the entries are in. Saved, so the app opens the way it was left.
/// </summary>
public sealed record SidebarLayout
{
    public bool Hidden { get; init; }

    /// <summary>Groups folded up to just their heading.</summary>
    public IReadOnlyList<string> Folded { get; init; } = [];

    /// <summary>
    /// Every entry top to bottom, pages and headings, once the person has moved something. Empty means the
    /// app's own order. A heading's key starts with <see cref="Sidebar.HeadingMark"/>.
    /// </summary>
    public IReadOnlyList<string> Order { get; init; } = [];

    public bool IsOpen(string group) => !Folded.Contains(group, StringComparer.Ordinal);
}

/// <summary>
/// The rules for the sidebar. Entries are keys: a page is its page name, a heading is "#" and the group's
/// name. A page belongs to the nearest heading above it; pages above the first heading belong to no group.
/// The last heading and what follows it (the spacer and Settings) stay at the bottom.
/// </summary>
public static class Sidebar
{
    public const string HeadingMark = "#";

    public static string Heading(string group) => HeadingMark + group;
    public static bool IsHeading(string key) => key.StartsWith(HeadingMark, StringComparison.Ordinal);

    public static SidebarLayout Toggle(SidebarLayout layout) => layout with { Hidden = !layout.Hidden };

    /// <summary>Folds a group up or opens it out. A group not on the sidebar is left alone.</summary>
    public static SidebarLayout Fold(SidebarLayout layout, string group, bool open)
    {
        if (open)
            return layout.IsOpen(group) ? layout : layout with { Folded = layout.Folded.Where(g => g != group).ToList() };
        return layout.IsOpen(group) ? layout with { Folded = [.. layout.Folded, group] } : layout;
    }

    /// <summary>Back to the app's own order with every group open. Hidden or showing stays as it is.</summary>
    public static SidebarLayout Reset(SidebarLayout layout) => layout with { Order = [], Folded = [] };

    /// <summary>
    /// The entries top to bottom: the saved order where there is one, with anything the app has added since
    /// slotted in after the entry it follows by default, and anything that no longer exists dropped.
    /// </summary>
    public static List<string> Resolve(SidebarLayout layout, IReadOnlyList<string> defaults)
    {
        var order = layout.Order.Where(defaults.Contains).Distinct().ToList();
        if (order.Count == 0)
            return defaults.ToList();
        for (var i = 0; i < defaults.Count; i++)
        {
            if (order.Contains(defaults[i]))
                continue;
            var after = i == 0 ? -1 : order.IndexOf(defaults[i - 1]);
            order.Insert(after + 1, defaults[i]);
        }
        return order;
    }

    /// <summary>The heading a page sits under, or null above the first heading.</summary>
    public static string? GroupOf(IReadOnlyList<string> order, string page)
    {
        string? group = null;
        foreach (var key in order)
        {
            if (IsHeading(key))
                group = key[HeadingMark.Length..];
            else if (key == page)
                return group;
        }
        return null;
    }

    public static bool CanMove(SidebarLayout layout, IReadOnlyList<string> defaults, string key, int by) =>
        Moved(Resolve(layout, defaults), key, by) is not null;

    /// <summary>
    /// Moves a page one place up (-1) or down (+1), past headings too, so it can change group. Moves a
    /// heading with its whole group, past the neighbouring group. Nothing moves into or out of the bottom.
    /// </summary>
    public static SidebarLayout Move(SidebarLayout layout, IReadOnlyList<string> defaults, string key, int by)
    {
        var moved = Moved(Resolve(layout, defaults), key, by);
        return moved is null ? layout : layout with { Order = moved };
    }

    private static List<string>? Moved(List<string> order, string key, int by)
    {
        var at = order.IndexOf(key);
        var pinned = order.FindLastIndex(IsHeading);
        if (at < 0 || by == 0 || pinned < 0 || at >= pinned)
            return null;
        var step = Math.Sign(by);
        if (!IsHeading(key))
        {
            var to = at + step;
            if (to < 0 || to >= pinned)
                return null;
            (order[at], order[to]) = (order[to], order[at]);
            return order;
        }
        // A heading takes its pages with it and swaps with the group next to it.
        var firstHeading = order.FindIndex(IsHeading);
        var blocks = Blocks(order, firstHeading, pinned);
        var block = blocks.FindIndex(b => b.Start == at);
        var other = block + step;
        if (other < 0 || other >= blocks.Count)
            return null;
        var (first, second) = step < 0 ? (blocks[other], blocks[block]) : (blocks[block], blocks[other]);
        var result = order.Take(first.Start)
            .Concat(order.Skip(second.Start).Take(second.Length))
            .Concat(order.Skip(first.Start).Take(first.Length))
            .Concat(order.Skip(second.Start + second.Length))
            .ToList();
        return result;
    }

    // The movable groups: each heading with its pages, from the first heading up to the pinned one.
    private static List<(int Start, int Length)> Blocks(List<string> order, int from, int to)
    {
        var blocks = new List<(int, int)>();
        var start = from;
        for (var i = from + 1; i <= to; i++)
        {
            if (IsHeading(order[i]))
            {
                blocks.Add((start, i - start));
                start = i;
            }
        }
        return blocks;
    }

    /// <summary>A saved layout against the sidebar as it is now: a group that no longer exists is forgotten.</summary>
    public static SidebarLayout Reconcile(SidebarLayout? saved, params string[] groups)
    {
        if (saved is null)
            return new SidebarLayout();
        return saved with { Folded = saved.Folded.Where(g => groups.Contains(g, StringComparer.Ordinal)).Distinct().ToList() };
    }
}
