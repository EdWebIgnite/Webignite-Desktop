using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

// The other two ways to reach pages besides the sidebar (Settings, Look and feel, Menu): one Menu button that opens
// the whole menu over the page, or a dock of the pages used most along the bottom with the rest behind More. Both
// list what the sidebar lists, in the person's own order, and pick through SelectedNav as the sidebar does, so a
// page, a tool, a project or one of My pages opens the same way from any of the three.
public sealed partial class MainViewModel
{
    public MenuKind Menu => services.Settings.Appearance.Menu;
    public bool IsSidebarMenu => Menu == MenuKind.Sidebar;
    public bool IsButtonMenu => Menu == MenuKind.Button;
    public bool IsDockMenu => Menu == MenuKind.Dock;

    /// <summary>Without the sidebar, a bar along the top holds what the sidebar's top and foot held: search, chat, the timer.</summary>
    public bool HasMenuBar => !IsSidebarMenu;

    /// <summary>The whole menu is open over the page.</summary>
    [ObservableProperty] public partial bool IsMenuOpen { get; set; }

    /// <summary>The menu's columns, made each time it opens, so figures and the person's order are as they are now.</summary>
    public ObservableCollection<MenuColumn> MenuColumns { get; } = [];

    /// <summary>While editing: what has been taken off the menus, to put back with a click.</summary>
    public ObservableCollection<MenuEntry> MenuAvailable { get; } = [];

    public bool NothingAvailable => MenuAvailable.Count == 0;

    /// <summary>The pages on the dock.</summary>
    public ObservableCollection<MenuEntry> Dock { get; } = [];

    private void MenuKindChanged()
    {
        IsMenuOpen = false;
        foreach (var name in new[] { nameof(Menu), nameof(IsSidebarMenu), nameof(IsButtonMenu), nameof(IsDockMenu), nameof(HasMenuBar) })
            OnPropertyChanged(name);
        FillDock();
    }

    [RelayCommand]
    private void ToggleMenu()
    {
        if (IsSidebarMenu)
            return;
        if (!IsMenuOpen)
            FillMenu();
        IsMenuOpen = !IsMenuOpen;
    }

    // However the menu is closed (Esc, a click beside it), editing ends with it.
    partial void OnIsMenuOpenChanged(bool value)
    {
        if (!value)
            IsMenuEditing = false;
    }

    [RelayCommand]
    private void CloseMenu()
    {
        IsMenuOpen = false;
        IsMenuEditing = false;
    }

    // Choosing what is on the menus, as a phone's quick settings are edited: the menu opens with a minus on each
    // entry, and what has been taken off waits under Available with a plus. It works from all three kinds of menu.

    /// <summary>The open menu is being edited: a click takes an entry off or puts it back, and entries can be dragged.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMenuBrowsing))]
    public partial bool IsMenuEditing { get; set; }

    public bool IsMenuBrowsing => !IsMenuEditing;

    /// <summary>True when something has been taken off, so Put everything back has something to do.</summary>
    public bool HasMenuTakenOff => Sidebar.HasTakenOff;

    [RelayCommand]
    private void EditMenu()
    {
        IsMenuEditing = true;
        FillMenu();
        IsMenuOpen = true;
    }

    /// <summary>Done: back to the menu as it is used, or closed when it was only opened to edit (from the sidebar).</summary>
    [RelayCommand]
    private void DoneEditingMenu()
    {
        IsMenuEditing = false;
        if (IsSidebarMenu)
            IsMenuOpen = false;
        else
            FillMenu();
    }

    [RelayCommand]
    private void PutAllBackOnMenu() => Sidebar.PutAllBack();

    /// <summary>Drops an entry of the open menu beside another while editing; one that was off comes back on there.</summary>
    public void MoveInMenu(MenuEntry entry, MenuEntry target, bool below)
    {
        if (!CanMoveInMenu(entry, target))
            return;
        if (entry.IsOff)
            Sidebar.PutBack(entry.Item);
        Sidebar.MoveTo(entry.Item, target.Item, below);
    }

    public bool CanMoveInMenu(MenuEntry entry, MenuEntry target) =>
        IsMenuEditing && !ReferenceEquals(entry, target) && !target.IsOff && Sidebar.CanDrag(entry.Item) && Sidebar.CanDrag(target.Item)
        && !(entry.Item is Module { InPlanner: true }) && !(target.Item is Module { InPlanner: true });

    // What is on the menus changed: the dock drops what was taken off, and the open menu is drawn again.
    private void MenusChanged()
    {
        OnPropertyChanged(nameof(HasMenuTakenOff));
        FillDock();
        if (IsMenuOpen)
            FillMenu();
    }

    [RelayCommand]
    private void PickFromMenu(MenuEntry? entry)
    {
        if (entry is null)
            return;
        if (IsMenuEditing)
        {
            if (entry.IsOff)
                Sidebar.PutBack(entry.Item);
            else
                Sidebar.TakeOff(entry.Item);
            return;
        }
        IsMenuOpen = false;
        // The Planner entry opens Planner's Home; its own pages are in the menu's Planner column.
        SelectedNav = entry.Item is SectionLink ? this[Page.Home] : entry.Item;
    }

    [RelayCommand]
    private void AddToDock(MenuEntry? entry) => ChangeDock(entry, DockPages.Add);

    [RelayCommand]
    private void TakeOffDock(MenuEntry? entry) => ChangeDock(entry, DockPages.Remove);

    private void ChangeDock(MenuEntry? entry, Func<IReadOnlyList<string>, string, IReadOnlyList<string>> change)
    {
        if (entry?.PageKey is not { } key)
            return;
        services.Settings.Appearance = services.Settings.Appearance with { Dock = change(DockKeys(), key) };
        FillDock();
        if (IsMenuOpen)
            FillMenu();
    }

    // Pages taken off the menus are off the dock too.
    private IReadOnlyList<string> DockKeys() =>
        DockPages.Shown(services.Settings.Appearance.Dock, [.. Modules.Where(m => !Sidebar.IsOff(m)).Select(m => m.Key.ToString())]);

    /// <summary>Makes the dock's entries; called when the menu kind, the dock's pages or the page on screen changes.</summary>
    private void FillDock()
    {
        Dock.Clear();
        // While the app is still starting there is no page picked yet; the dock is filled once there is.
        if (!IsDockMenu || Selected is null)
            return;
        foreach (var key in DockKeys())
        {
            var module = Modules.First(m => m.Key.ToString() == key);
            // Planner's Home stands for the whole of Planner on the dock, so it is named for it.
            Dock.Add(new MenuEntry(module, module.Key == Page.Home ? "Planner" : module.Title, module.Icon)
            {
                PageKey = key,
                // A Planner page on the dock stays lit for the whole of Planner, as the sidebar's Planner entry does.
                IsCurrent = Selected == module || (module.Key == Page.Home && Selected.InPlanner && !DockKeys().Contains(Selected.Key.ToString())),
                Module = module,
                OnDock = true,
                CanTakeOff = DockPages.CanRemove(DockKeys(), key),
            });
        }
    }

    private void FillMenu()
    {
        // Read when the menu opens, so what it says is as things stand now.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var figures = MenuFigures.For(new MenuFacts
        {
            Today = today,
            Documents = services.Documents.List(),
            UnreadMail = this[Page.Mail].Badge,
            Renewals = services.Store.ListRenewals(),
            Retainers = services.Store.ListRetainers(),
            Drafted = services.Store.DraftsMade().Keys.ToHashSet(),
            ReceivedThisMonth = Webignite.Core.Finance.Ledger.Sum(services.Store.ListEntries(Webignite.Core.Dates.MonthStart(today), today), Webignite.Core.Finance.EntryKind.Received),
            Clients = services.Clients.List(),
        });
        var dock = DockKeys();
        MenuColumns.Clear();
        var column = new MenuColumn("Start");
        // While editing, every entry on the menus shows, whether its group is folded in the sidebar or not.
        foreach (var item in IsMenuEditing ? Sidebar.OnMenus() : Sidebar.Items)
        {
            if (item is NavGroup group)
            {
                Keep(column);
                // The space above Settings has no name; what follows it is about the app itself.
                column = new MenuColumn(group.CanFold ? group.Name : "Webignite");
                continue;
            }
            // Planner's own pages have a column of their own, below.
            if (item is not Module { InPlanner: true } && Entry(item) is { } entry)
                column.Entries.Add(entry);
        }
        Keep(column);
        // Planner's own pages and projects, which the sidebar shows in place of the main list.
        var planner = new MenuColumn("Planner");
        foreach (var item in Sidebar.PlannerItems)
            if (item is Module || (item is SubLink && !IsMenuEditing))
                if (Entry(item) is { } entry)
                    planner.Entries.Add(entry);
        Keep(planner);
        // What has been taken off, waiting to be put back.
        MenuAvailable.Clear();
        if (IsMenuEditing)
            foreach (var item in Sidebar.TakenOff())
                if (Entry(item) is { } entry)
                    MenuAvailable.Add(entry);
        OnPropertyChanged(nameof(NothingAvailable));

        void Keep(MenuColumn made)
        {
            if (made.Entries.Count > 0)
                MenuColumns.Add(made);
        }

        MenuEntry? Entry(object item) => Made(item) is { } made
            ? made with { Editing = IsMenuEditing, IsOff = Sidebar.IsOff(item), CanTakeOffMenu = Sidebar.CanTakeOff(item) }
            : null;

        MenuEntry? Made(object item)
        {
            switch (item)
            {
                case Module module:
                    var key = module.Key.ToString();
                    var figure = figures.GetValueOrDefault(key);
                    return new MenuEntry(module, module.Title, module.Icon)
                    {
                        PageKey = key,
                        Figure = figure?.Text ?? (module.HasBadge ? module.Badge.ToString(System.Globalization.CultureInfo.CurrentCulture) : ""),
                        Late = figure?.Late ?? false,
                        IsCurrent = Selected == module,
                        Module = module,
                        OnDock = dock.Contains(key),
                        CanAdd = IsDockMenu && DockPages.CanAdd(dock, key),
                        CanTakeOff = IsDockMenu && DockPages.CanRemove(dock, key),
                    };
                case ToolLink tool:
                    return new MenuEntry(tool, tool.Title, tool.Icon);
                case SectionLink section:
                    return new MenuEntry(section, section.Title, section.Icon) { IsCurrent = Selected.InPlanner };
                case SubLink link:
                    return new MenuEntry(link, link.Title, link.Icon) { IsCurrent = ReferenceEquals(SelectedNav, link) };
                default:
                    return null;
            }
        }
    }
}

/// <summary>A column of the open menu: a group's name and what is in it.</summary>
public sealed class MenuColumn(string title)
{
    public string Title { get; } = title;
    public ObservableCollection<MenuEntry> Entries { get; } = [];
}

/// <summary>One thing the menu or the dock opens: a page, a tool, a project or one of My pages.</summary>
public sealed record MenuEntry(object Item, string Title, string Icon)
{
    /// <summary>The menu is being edited: a click takes it off or puts it back.</summary>
    public bool Editing { get; init; }

    /// <summary>It has been taken off the menus.</summary>
    public bool IsOff { get; init; }

    /// <summary>It may be taken off (Settings and Planner's Home may not).</summary>
    public bool CanTakeOffMenu { get; init; }

    public bool ShowsMinus => Editing && !IsOff && CanTakeOffMenu;
    public bool ShowsPlus => Editing && IsOff;
    public bool ShowsFigure => !Editing && HasFigure;

    /// <summary>What a click does while editing, for the tip.</summary>
    public string? EditTip => !Editing ? null : IsOff ? "Put it back on the menu" : CanTakeOffMenu ? "Take it off the menu" : "This one always stays";

    /// <summary>The page's name, when it is a page (only pages go on the dock).</summary>
    public string? PageKey { get; init; }

    /// <summary>The page itself, for its live count (unread mail) on the dock.</summary>
    public Module? Module { get; init; }

    /// <summary>How things stand there, in a few words: "R 6 825 owed".</summary>
    public string Figure { get; init; } = "";
    public bool HasFigure => Figure.Length > 0;
    public bool Late { get; init; }

    /// <summary>It is what is on screen.</summary>
    public bool IsCurrent { get; init; }

    public bool OnDock { get; init; }
    public bool CanAdd { get; init; }
    public bool CanTakeOff { get; init; }
}
