using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Tools page: every small tool as a tile, under tabs (Computer, Web, Pictures and brand), with a box to find
/// one. A click opens the tool as the sidebar did. Its pin puts it on the menus as well, or takes it off: by
/// default the tools live only here, so the sidebar stays short.
/// </summary>
public sealed partial class ToolboxViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices services;

    public ToolboxViewModel(AppServices services)
    {
        this.services = services;
        Tabs = [.. Enum.GetValues<ToolTab>().Select(tab => new ToolTabChoice(tab, this))];
        Show();
    }

    public IReadOnlyList<ToolTabChoice> Tabs { get; }

    /// <summary>The tab showing. With something typed in the box, every tab is looked through instead.</summary>
    [ObservableProperty] public partial ToolTab Tab { get; set; } = ToolTab.Computer;

    /// <summary>What was typed to find a tool: words from its name or from what it does.</summary>
    [ObservableProperty] public partial string Find { get; set; } = "";

    public bool IsFinding => !string.IsNullOrWhiteSpace(Find);

    public ObservableCollection<ToolTile> Tiles { get; } = [];

    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>How many tools are pinned to the menus, said under the heading.</summary>
    public string Summary
    {
        get
        {
            var pinned = ToolCatalogue.All.Count(t => services.Go.IsOnMenus(t.MenuKey));
            return pinned switch
            {
                0 => "Everything in one place. Pin the ones you use most and they show in the sidebar too.",
                1 => "1 tool is pinned to the sidebar.",
                _ => $"{pinned} tools are pinned to the sidebar.",
            };
        }
    }

    partial void OnTabChanged(ToolTab value) => Show();

    partial void OnFindChanged(string value)
    {
        OnPropertyChanged(nameof(IsFinding));
        Show();
    }

    /// <summary>The menus may have changed elsewhere (a right-click in the sidebar): the pins are read again.</summary>
    public void Refresh() => Show();

    private void Show()
    {
        Tiles.Clear();
        var shown = IsFinding
            ? ToolCatalogue.All.Where(t => ToolShelf.Matches(Find, t.Title, t.About))
            : ToolCatalogue.All.Where(t => t.Tab == Tab);
        foreach (var entry in shown)
            Tiles.Add(new ToolTile(entry, services.Go.IsOnMenus(entry.MenuKey), this));
        foreach (var tab in Tabs)
            tab.IsOn = !IsFinding && tab.Tab == Tab;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    internal void Open(ToolEntry entry)
    {
        if (entry.Kind is { } kind)
            services.Go.Tool(kind);
        else if (entry.Page is { } page)
            services.Go.To(page);
    }

    internal void Pin(ToolTile tile)
    {
        services.Go.ShowOnMenus(tile.Entry.MenuKey, !tile.IsPinned);
        tile.IsPinned = services.Go.IsOnMenus(tile.Entry.MenuKey);
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void ClearFind() => Find = "";
}

/// <summary>A tab on the Tools page.</summary>
public sealed partial class ToolTabChoice(ToolTab tab, ToolboxViewModel page) : ObservableObject
{
    public ToolTab Tab { get; } = tab;
    public string Title { get; } = ToolCatalogue.TabTitle(tab);

    [ObservableProperty] public partial bool IsOn { get; set; }

    [RelayCommand]
    private void Pick()
    {
        page.Find = "";
        page.Tab = Tab;
    }
}

/// <summary>One tool on the Tools page: click to open it, pin to keep it on the menus too.</summary>
public sealed partial class ToolTile(ToolEntry entry, bool pinned, ToolboxViewModel page) : ObservableObject
{
    public ToolEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public string About => Entry.About;
    public string Icon => Entry.Icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinTip))]
    public partial bool IsPinned { get; set; } = pinned;

    public string PinTip => IsPinned ? "Pinned: it shows in the sidebar too. Click to take it off." : "Pin it to the sidebar";

    [RelayCommand]
    private void Open() => page.Open(Entry);

    [RelayCommand]
    private void Pin() => page.Pin(this);
}
