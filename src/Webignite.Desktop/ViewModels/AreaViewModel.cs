using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The page behind one of the tabs across the top (Business, Computer, Tools): everything in that area as tiles
/// under a few headings, with a box that finds a page or tool anywhere in the app. A click opens it. Its pin puts it
/// on the sidebar, which is the person's own shortlist, or takes it off again.
/// </summary>
public sealed partial class AreaViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices services;

    public AreaViewModel(AppServices services, AppArea area)
    {
        this.services = services;
        Area = area;
        Show();
    }

    public AppArea Area { get; }

    public string Title => ToolCatalogue.AreaTitle(Area);

    /// <summary>What was typed to find a page or tool: words from its name or from what it does, across every area.</summary>
    [ObservableProperty] public partial string Find { get; set; } = "";

    public bool IsFinding => !string.IsNullOrWhiteSpace(Find);

    /// <summary>The headings on the page, each with its tiles; while finding, one group of everything that fits.</summary>
    public ObservableCollection<AreaGroup> Groups { get; } = [];

    public bool IsEmpty => Groups.Count == 0;

    /// <summary>How many of this area's things are pinned, said under the heading.</summary>
    public string Summary
    {
        get
        {
            var pinned = ToolCatalogue.All.Count(t => t.Area == Area && services.Go.IsOnMenus(t.MenuKey));
            return pinned switch
            {
                0 => "Pin what you use most and it shows in the sidebar.",
                1 => "1 of these is pinned to the sidebar.",
                _ => $"{pinned} of these are pinned to the sidebar.",
            };
        }
    }

    partial void OnFindChanged(string value)
    {
        OnPropertyChanged(nameof(IsFinding));
        Show();
    }

    /// <summary>The sidebar may have changed elsewhere (a right-click on it): the pins are read again.</summary>
    public void Refresh() => Show();

    private void Show()
    {
        Groups.Clear();
        if (IsFinding)
        {
            var found = ToolCatalogue.All.Where(t => ToolShelf.Matches(Find, t.Title, t.About)).Select(Tile).ToList();
            if (found.Count > 0)
                Groups.Add(new AreaGroup("Found", found));
        }
        else
        {
            foreach (var heading in ToolCatalogue.All.Where(t => t.Area == Area).GroupBy(t => t.Heading))
                Groups.Add(new AreaGroup(heading.Key, [.. heading.Select(Tile)]));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    private AreaTile Tile(AppEntry entry) => new(entry, services.Go.IsOnMenus(entry.MenuKey), this);

    internal void Open(AppEntry entry)
    {
        if (entry.Kind is { } kind)
            services.Go.Tool(kind);
        else if (entry.Section is not null)
            services.Go.To(Page.Home); // Planner opens on its Home, with its own sidebar
        else if (entry.Page is { } page)
            services.Go.To(page);
    }

    internal void Pin(AreaTile tile)
    {
        services.Go.ShowOnMenus(tile.Entry.MenuKey, !tile.IsPinned);
        tile.IsPinned = services.Go.IsOnMenus(tile.Entry.MenuKey);
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>A heading on an area's page with the tiles under it.</summary>
public sealed record AreaGroup(string Heading, IReadOnlyList<AreaTile> Tiles);

/// <summary>One page or tool on an area's page: click to open it, pin to keep it on the sidebar.</summary>
public sealed partial class AreaTile(AppEntry entry, bool pinned, AreaViewModel page) : ObservableObject
{
    public AppEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public string About => Entry.About;
    public string Icon => Entry.Icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinTip))]
    public partial bool IsPinned { get; set; } = pinned;

    public string PinTip => IsPinned ? "Pinned: it shows in the sidebar. Click to take it off." : "Pin it to the sidebar";

    [RelayCommand]
    private void Open() => page.Open(Entry);

    [RelayCommand]
    private void Pin() => page.Pin(this);
}

/// <summary>One of the tabs across the top of the window.</summary>
public sealed partial class AreaTab(AppArea area, Action<AppArea> pick) : ObservableObject
{
    public AppArea Area { get; } = area;
    public string Title { get; } = ToolCatalogue.AreaTitle(area);
    public string Icon { get; } = ToolCatalogue.AreaIcon(area);

    /// <summary>The page on screen belongs to this area.</summary>
    [ObservableProperty] public partial bool IsOn { get; set; }

    [RelayCommand]
    private void Pick() => pick(Area);
}
