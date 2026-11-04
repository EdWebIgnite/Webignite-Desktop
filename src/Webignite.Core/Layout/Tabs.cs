namespace Webignite.Core.Layout;

/// <summary>The tabs that were open, by page name, and which one was showing. Saved under "tabs".</summary>
public sealed record TabSettings
{
    public IReadOnlyList<string> Pages { get; init; } = [];
    public int Active { get; init; }

    /// <summary>Pages popped out into windows of their own, and where those windows were.</summary>
    public IReadOnlyList<PopOutPlacement> PoppedOut { get; init; } = [];
}

/// <summary>A popped-out window: its page and where it was on screen.</summary>
public sealed record PopOutPlacement(string Page, int X, int Y, double Width, double Height, bool Maximized = false);

/// <summary>How the tab strip behaves: bringing tabs back, moving between them, and which shows after a close.</summary>
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
        return pages.Count == 0
            ? new TabSettings { Pages = [home], PoppedOut = poppedOut }
            : new TabSettings { Pages = pages, Active = Math.Clamp(active, 0, pages.Count - 1), PoppedOut = poppedOut };
    }

    /// <summary>The tab one step on (Ctrl+Tab) or back (Ctrl+Shift+Tab), going round at the ends.</summary>
    public static int Step(int index, int count, int by) => count <= 0 ? 0 : ((index + by) % count + count) % count;

    /// <summary>
    /// Which tab shows after closing one: the one that took its place on the right, or the new last one when
    /// the last was closed. Only matters when the closed tab was showing.
    /// </summary>
    public static int AfterClose(int closed, int countAfter) => countAfter <= 0 ? 0 : Math.Min(closed, countAfter - 1);
}
