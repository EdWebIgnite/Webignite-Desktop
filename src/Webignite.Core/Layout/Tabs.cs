namespace Webignite.Core.Layout;

/// <summary>The tabs that were open, by page name, and which one was showing. Saved under "tabs".</summary>
public sealed record TabSettings
{
    public IReadOnlyList<string> Pages { get; init; } = [];
    public int Active { get; init; }

    /// <summary>Pages popped out into windows of their own, and where those windows were.</summary>
    public IReadOnlyList<PopOutPlacement> PoppedOut { get; init; } = [];

    /// <summary>The page shown beside the main one when the window is split, if it is.</summary>
    public string? Beside { get; init; }

    public bool BesideOnLeft { get; init; }

    /// <summary>How much of the width the main page takes when split, from 0.2 to 0.8.</summary>
    public double MainShare { get; init; } = 0.5;
}

/// <summary>A popped-out window: its page and where it was on screen.</summary>
public sealed record PopOutPlacement(string Page, int X, int Y, double Width, double Height, bool Maximized = false);

/// <summary>How the tab strip behaves: bringing tabs back, moving between them, and which shows after a close.</summary>
/// <summary>What a dragged side of a split does when let go.</summary>
public enum SideDrop
{
    Nothing,
    Swap,
    PopOut,
    BackToTab,
}

public static class TabRules
{
    /// <summary>
    /// The tabs to bring back: only pages this version knows, and at least the home page. The active one is
    /// kept if it is still there, else the first.
    /// </summary>
    public static TabSettings Restore(TabSettings saved, IReadOnlySet<string> known, string home)
    {
        var pages = new List<string>();
        var active = 0;
        for (var i = 0; i < saved.Pages.Count; i++)
        {
            if (!known.Contains(saved.Pages[i]))
                continue;
            if (i == saved.Active)
                active = pages.Count;
            pages.Add(saved.Pages[i]);
        }
        var poppedOut = saved.PoppedOut.Where(p => known.Contains(p.Page) && p.Width > 0 && p.Height > 0).ToList();
        var beside = saved.Beside is { } b && known.Contains(b) ? b : null;
        return new TabSettings
        {
            Pages = pages.Count == 0 ? [home] : pages,
            Active = pages.Count == 0 ? 0 : Math.Clamp(active, 0, pages.Count - 1),
            PoppedOut = poppedOut,
            Beside = beside,
            BesideOnLeft = beside is not null && saved.BesideOnLeft,
            MainShare = Share(saved.MainShare),
        };
    }

    /// <summary>The main page's share of the width when split, kept so neither side gets too narrow to use.</summary>
    public static double Share(double share) => double.IsFinite(share) ? Math.Clamp(share, 0.2, 0.8) : 0.5;

    /// <summary>
    /// Where a tab let go over the page area goes: to the left or right side when it is near that edge
    /// (the outer fifth), or nowhere (null) in the middle.
    /// </summary>
    public static bool? SideAt(double x, double width) =>
        width <= 0 ? null : x < width * 0.2 ? true : x > width * 0.8 ? false : null;

    /// <summary>
    /// Where a side of a split goes when its header is dragged and let go, measured in the page area: back to a
    /// tab over the tabs (only the page beside can), into a window of its own when let go outside the window or
    /// well down into the page, or to the other side when let go over it. A little wobble does nothing.
    /// </summary>
    public static SideDrop DropSide(double x, double y, double width, double height, double fromX, double fromY,
        bool onLeft, bool overTabs, bool canBeTab)
    {
        if (Math.Abs(x - fromX) + Math.Abs(y - fromY) < 12)
            return SideDrop.Nothing;
        if (overTabs && canBeTab)
            return SideDrop.BackToTab;
        if (x < 0 || y < -40 || x > width || y > height || y - fromY > 120)
            return SideDrop.PopOut;
        return (onLeft ? x > width / 2 : x < width / 2) ? SideDrop.Swap : SideDrop.Nothing;
    }

    /// <summary>The tab one step on (Ctrl+Tab) or back (Ctrl+Shift+Tab), going round at the ends.</summary>
    public static int Step(int index, int count, int by) => count <= 0 ? 0 : ((index + by) % count + count) % count;

    /// <summary>
    /// Which tab shows after closing one: the one that took its place on the right, or the new last one when
    /// the last was closed. Only matters when the closed tab was showing.
    /// </summary>
    public static int AfterClose(int closed, int countAfter) => countAfter <= 0 ? 0 : Math.Min(closed, countAfter - 1);
}
