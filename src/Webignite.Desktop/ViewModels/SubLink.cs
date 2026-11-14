using CommunityToolkit.Mvvm.ComponentModel;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// An entry in the sidebar that opens part of a page rather than a page of its own: a project, My tasks, the
/// templates, or one of the person's own pages. One with no icon shows a small dot, set in under its page.
/// </summary>
public abstract partial class SubLink(string? id, string title, string icon) : ObservableObject
{
    /// <summary>The project's or page's id, or null for an entry like My tasks.</summary>
    public string? Id { get; } = id;

    [ObservableProperty] public partial string Title { get; set; } = title;

    /// <summary>The key of its line icon in Styles/Icons.axaml; empty for a dot.</summary>
    public string Icon { get; } = icon;

    public bool HasIcon => Icon.Length > 0 && !HasTile;
    public bool IsDot => Icon.Length == 0 && !HasTile;

    /// <summary>A project's coloured square: its colour name, empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTile), nameof(HasIcon), nameof(IsDot))]
    public partial string TileColour { get; set; } = "";

    /// <summary>The small picture on the coloured square.</summary>
    [ObservableProperty] public partial string TileIcon { get; set; } = "";

    public bool HasTile => TileColour.Length > 0;
}

/// <summary>One of the person's own pages in the sidebar, or the entry that makes a new one (no id).</summary>
public sealed class MyPageLink(string? id, string title, string icon) : SubLink(id, title, icon);

/// <summary>
/// A client or a project that some of the person's pages are for, as a heading in the sidebar's My pages group
/// with those pages under it. A click opens the client or the project.
/// </summary>
public sealed class MyPageGroupLink(Webignite.Core.Layout.PageGroupKind kind, string id, string title)
    : SubLink(id, title, kind == Webignite.Core.Layout.PageGroupKind.Client ? "Clients" : "Projects")
{
    public Webignite.Core.Layout.PageGroupKind Kind { get; } = kind;
}
