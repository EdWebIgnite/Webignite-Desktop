namespace Webignite.Core.Layout;

/// <summary>
/// How the sidebar was left: tucked away or showing, and which groups of pages are folded up.
/// Saved, so the app opens the way it was left.
/// </summary>
public sealed record SidebarLayout
{
    public bool Hidden { get; init; }

    /// <summary>Groups folded up to just their heading.</summary>
    public IReadOnlyList<string> Folded { get; init; } = [];

    public bool IsOpen(string group) => !Folded.Contains(group, StringComparer.Ordinal);
}

public static class Sidebar
{
    public static SidebarLayout Toggle(SidebarLayout layout) => layout with { Hidden = !layout.Hidden };

    /// <summary>Folds a group up or opens it out. A group not on the sidebar is left alone.</summary>
    public static SidebarLayout Fold(SidebarLayout layout, string group, bool open)
    {
        if (open)
            return layout.IsOpen(group) ? layout : layout with { Folded = layout.Folded.Where(g => g != group).ToList() };
        return layout.IsOpen(group) ? layout with { Folded = [.. layout.Folded, group] } : layout;
    }

    /// <summary>A saved layout against the groups the sidebar has now: a group that no longer exists is forgotten.</summary>
    public static SidebarLayout Reconcile(SidebarLayout? saved, params string[] groups)
    {
        if (saved is null)
            return new SidebarLayout();
        return saved with { Folded = saved.Folded.Where(g => groups.Contains(g, StringComparer.Ordinal)).Distinct().ToList() };
    }
}
