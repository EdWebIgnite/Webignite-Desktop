namespace Webignite.Core.Layout;

/// <summary>
/// How the panes of a page are laid out: their order left to right, how wide each one is, and which are
/// hidden. Saved per page, so the app opens the way it was left.
/// </summary>
public sealed record PanelLayout
{
    /// <summary>Every pane the page has, left to right, hidden ones included.</summary>
    public IReadOnlyList<string> Order { get; init; } = [];

    /// <summary>Widths in pixels for panes that were dragged. A pane not here keeps its usual width.</summary>
    public IReadOnlyDictionary<string, double> Widths { get; init; } = new Dictionary<string, double>();

    public IReadOnlyList<string> Hidden { get; init; } = [];

    public bool IsShown(string pane) => !Hidden.Contains(pane, StringComparer.Ordinal);
}

public static class Panels
{
    /// <summary>Narrower than this and a pane is no use; dragging stops here.</summary>
    public const double MinWidth = 160;

    public static PanelLayout Default(params string[] panes) => new() { Order = panes };

    /// <summary>The panes on screen, left to right.</summary>
    public static List<string> Visible(PanelLayout layout) => layout.Order.Where(layout.IsShown).ToList();

    /// <summary>The last pane showing can't be hidden, or there would be nothing left on the page.</summary>
    public static bool CanHide(PanelLayout layout, string pane) => layout.IsShown(pane) && Visible(layout).Count > 1;

    public static PanelLayout Show(PanelLayout layout, string pane, bool show)
    {
        if (!layout.Order.Contains(pane, StringComparer.Ordinal))
            return layout;
        if (show)
            return layout with { Hidden = layout.Hidden.Where(h => h != pane).ToList() };
        if (!CanHide(layout, pane))
            return layout;
        return layout with { Hidden = [.. layout.Hidden, pane] };
    }

    /// <summary>Moves a pane one place left (-1) or right (+1), past hidden panes so the change is visible.</summary>
    public static PanelLayout Move(PanelLayout layout, string pane, int by)
    {
        var visible = Visible(layout);
        var at = visible.IndexOf(pane);
        if (at < 0 || by == 0)
            return layout;
        var to = Math.Clamp(at + Math.Sign(by), 0, visible.Count - 1);
        if (to == at)
            return layout;
        // Swap with its visible neighbour, keeping hidden panes where they were.
        var other = visible[to];
        var order = layout.Order.ToList();
        var i = order.IndexOf(pane);
        var j = order.IndexOf(other);
        (order[i], order[j]) = (order[j], order[i]);
        return layout with { Order = order };
    }

    public static bool CanMove(PanelLayout layout, string pane, int by)
    {
        var visible = Visible(layout);
        var at = visible.IndexOf(pane);
        return at >= 0 && at + Math.Sign(by) >= 0 && at + Math.Sign(by) < visible.Count;
    }

    /// <summary>Remembers a dragged width, never narrower than <see cref="MinWidth"/>.</summary>
    public static PanelLayout Resize(PanelLayout layout, string pane, double width)
    {
        if (!layout.Order.Contains(pane, StringComparer.Ordinal) || double.IsNaN(width) || double.IsInfinity(width))
            return layout;
        var widths = new Dictionary<string, double>(layout.Widths) { [pane] = Math.Round(Math.Max(MinWidth, width)) };
        return layout with { Widths = widths };
    }

    /// <summary>
    /// A saved layout against the panes the page has now: panes added since go on the right, panes that no
    /// longer exist are dropped. The result always has every current pane exactly once.
    /// </summary>
    public static PanelLayout Reconcile(PanelLayout? saved, params string[] panes)
    {
        if (saved is null)
            return Default(panes);
        var order = saved.Order.Where(p => panes.Contains(p, StringComparer.Ordinal)).Distinct().ToList();
        order.AddRange(panes.Where(p => !order.Contains(p, StringComparer.Ordinal)));
        var layout = saved with
        {
            Order = order,
            Hidden = saved.Hidden.Where(h => panes.Contains(h, StringComparer.Ordinal)).Distinct().ToList(),
            Widths = saved.Widths.Where(w => panes.Contains(w.Key, StringComparer.Ordinal)).ToDictionary(w => w.Key, w => Math.Max(MinWidth, w.Value)),
        };
        return Visible(layout).Count == 0 ? layout with { Hidden = [] } : layout;
    }
}
