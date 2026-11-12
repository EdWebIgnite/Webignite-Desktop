using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

// The other two ways to reach pages besides the sidebar (Settings, Look and feel, Menu): one Menu button that opens
// the whole menu over the page, or a dock of the pages used most along the bottom with the rest behind More. Both
// list what the sidebar lists, in the person's own order, and pick through SelectedNav as the sidebar does, so a
// page, a tool, a project or one of My pages opens the same way from any of the three.
public sealed partial class MainMenuPart(AppServices services, MainViewModel main) : ViewModelBase
{
    /// <summary>Planner as a group for the dock. It has no heading in the sidebar, so it is made here.</summary>
    private static readonly NavGroup PlannerGroup = new("planner", "PLANNER") { Icon = "Home" };

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

    private MenuKind? lastKind;

    /// <summary>The look changed. Only a change of the menu's own kind closes the open menu; adding to the dock leaves it open.</summary>
    internal void MenuKindChanged()
    {
        if (lastKind is { } was && was != Menu)
            IsMenuOpen = false;
        lastKind = Menu;
        foreach (var name in new[] { nameof(Menu), nameof(IsSidebarMenu), nameof(IsButtonMenu), nameof(IsDockMenu), nameof(HasMenuBar) })
            OnPropertyChanged(name);
        FillDock();
        main.DockBarPart.FillBar();
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
    public bool HasMenuTakenOff => main.Sidebar.HasTakenOff;

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
    private void PutAllBackOnMenu() => main.Sidebar.PutAllBack();

    /// <summary>Drops an entry of the open menu beside another while editing; one that was off comes back on there.</summary>
    public void MoveInMenu(MenuEntry entry, MenuEntry target, bool below)
    {
        if (!CanMoveInMenu(entry, target))
            return;
        if (entry.IsOff)
            main.Sidebar.PutBack(entry.Item);
        main.Sidebar.MoveTo(entry.Item, target.Item, below);
    }

    public bool CanMoveInMenu(MenuEntry entry, MenuEntry target) =>
        IsMenuEditing && !ReferenceEquals(entry, target) && !target.IsOff && main.Sidebar.CanDrag(entry.Item) && main.Sidebar.CanDrag(target.Item)
        && !(entry.Item is Module { InPlanner: true }) && !(target.Item is Module { InPlanner: true });

    // What is on the menus changed: the dock drops what was taken off, and the open menu is drawn again.
    internal void MenusChanged()
    {
        OnPropertyChanged(nameof(HasMenuTakenOff));
        FillDock();
        if (IsMenuOpen)
            FillMenu();
    }

    [RelayCommand]
    private void PickFromMenu(MenuEntry? entry)
    {
        // A group on the dock opens its own short list; there is nothing to go to.
        if (entry is null || entry.Item is NavGroup or NavHeading)
            return;
        if (IsMenuEditing)
        {
            if (entry.IsOff)
                main.Sidebar.PutBack(entry.Item);
            else
                main.Sidebar.TakeOff(entry.Item);
            return;
        }
        IsMenuOpen = false;
        // The Planner entry opens Planner's Home; its own pages are in the menu's Planner column.
        main.SelectedNav = entry.Item is SectionLink ? main[Page.Home] : entry.Item;
    }

    [RelayCommand]
    private void AddToDock(MenuEntry? entry) => ChangeDock(entry, DockPages.Add);

    [RelayCommand]
    private void TakeOffDock(MenuEntry? entry) => ChangeDock(entry, DockPages.Remove);

    /// <summary>Drags one along the dock, to just before or after another.</summary>
    public void MoveOnDock(MenuEntry entry, MenuEntry beside, bool after)
    {
        if (entry.PageKey is not { } key || beside.PageKey is not { } target)
            return;
        var now = DockNow();
        var moved = DockPages.Move(now, key, target, after);
        if (!moved.SequenceEqual(now))
            services.Settings.Appearance = services.Settings.Appearance with { Dock = moved };
    }

    /// <summary>
    /// Drags a page or tool to another place inside a group's list on the dock. It is the same order the sidebar and
    /// the menu behind More keep, so it changes there too.
    /// </summary>
    public void MoveInGroup(MenuEntry entry, MenuEntry beside, bool below)
    {
        if (CanMoveInGroup(entry, beside))
            main.Sidebar.MoveTo(entry.Item, beside.Item, below);
    }

    public bool CanMoveInGroup(MenuEntry entry, MenuEntry beside) =>
        !ReferenceEquals(entry, beside) && !entry.IsSection && !beside.IsSection && entry.Item is Module or ToolLink && beside.Item is Module or ToolLink
        && main.Sidebar.CanDrag(entry.Item) && main.Sidebar.CanDrag(beside.Item)
        && entry.Item is Module { InPlanner: true } == beside.Item is Module { InPlanner: true };

    /// <summary>The entry on the dock by its name there, as it stands now; null when it has been taken off.</summary>
    public MenuEntry? OnDock(string key) => Dock.FirstOrDefault(e => e.PageKey == key);

    private void ChangeDock(MenuEntry? entry, Func<IReadOnlyList<string>, string, IReadOnlyList<string>> change)
    {
        if (entry?.PageKey is not { } key)
            return;
        var now = DockNow();
        var changed = change(now, key);
        // Planner as a group takes the place of Planner's own button, which only opened its Home: one Planner, not two.
        var home = Page.Home.ToString();
        if (key == DockKeys.Group(PlannerGroup.Key) && !now.Contains(key) && now.Contains(home))
            changed = [.. now.Select(k => k == home ? key : k)];
        services.Settings.Appearance = services.Settings.Appearance with { Dock = changed };
        FillDock();
        if (IsMenuOpen)
            FillMenu();
    }

    // Pages and tools taken off the menus are off the dock too. A group stays while it has a name to show.
    private IReadOnlyList<string> DockNow() => DockPages.Shown(services.Settings.Appearance.Dock,
    [
        .. main.Modules.Where(m => !main.Sidebar.IsOff(m)).Select(m => m.Key.ToString()),
        .. main.Sidebar.OnMenus().OfType<ToolLink>().Select(t => DockKeys.Tool(t.Kind.ToString())),
        .. main.Sidebar.OnMenus().OfType<NavGroup>().Where(g => g.CanFold).Select(g => DockKeys.Group(g.Key)),
        DockKeys.Group(PlannerGroup.Key),
    ]);

    /// <summary>Makes the dock's entries; called when the menu kind, what is on the dock or the page on screen changes.</summary>
    internal void FillDock()
    {
        lastKind ??= Menu; // the kind the app started with, so the first change of kind is seen as one
        Dock.Clear();
        // While the app is still starting there is no page picked yet; the dock is filled once there is.
        if (!IsDockMenu || main.Selected is null)
            return;
        var keys = DockNow();
        var onMenus = main.Sidebar.OnMenus();
        foreach (var key in keys)
        {
            var canTakeOff = DockPages.CanRemove(keys, key);
            if (DockKeys.AsTool(key) is { } kind)
            {
                var tool = onMenus.OfType<ToolLink>().First(t => t.Kind.ToString() == kind);
                Dock.Add(new MenuEntry(tool, tool.Title, tool.Icon) { PageKey = key, OnDock = true, CanTakeOff = canTakeOff });
            }
            else if (DockKeys.AsGroup(key) is { } groupKey)
            {
                // A whole group: a click opens a short list of what is in it, just above the dock.
                // Planner isn't a heading in the sidebar: its pages are what the sidebar shows once Planner is opened.
                var isPlanner = groupKey == PlannerGroup.Key;
                var group = isPlanner ? PlannerGroup : onMenus.OfType<NavGroup>().First(g => g.Key == groupKey);
                // Planner's list is Planner's own menu, as its sidebar has it: Home and Inbox, the projects under
                // Work, then the planning pages under Plan.
                var inside = isPlanner
                    ? main.Sidebar.PlannerItems.Select(PlannerEntry).OfType<MenuEntry>().ToList()
                    : main.Sidebar.Unfolded().SkipWhile(i => !ReferenceEquals(i, group)).Skip(1).TakeWhile(i => i is not NavGroup)
                        .Select(i => Plain(i, keys)).OfType<MenuEntry>().ToList();
                Dock.Add(new MenuEntry(group, group.Name, group.Icon)
                {
                    PageKey = key, OnDock = true, CanTakeOff = canTakeOff, Inside = inside, IsCurrent = inside.Any(e => e.IsCurrent),
                });
            }
            else
            {
                var module = main.Modules.First(m => m.Key.ToString() == key);
                // Planner's Home stands for the whole of Planner on the dock, so it is named for it.
                Dock.Add(new MenuEntry(module, module.Key == Page.Home ? "Planner" : module.Title, module.Icon)
                {
                    PageKey = key,
                    // A Planner page on the dock stays lit for the whole of Planner, as the sidebar's Planner entry does.
                    IsCurrent = main.Selected == module || (module.Key == Page.Home && main.Selected.InPlanner && !keys.Contains(main.Selected.Key.ToString())),
                    Module = module,
                    OnDock = true,
                    CanTakeOff = canTakeOff,
                });
            }
        }
    }

    private MenuEntry? PlannerEntry(object item) => item switch
    {
        Module module when !main.Sidebar.IsOff(module) => new MenuEntry(module, module.Title, module.Icon)
            { PageKey = module.Key.ToString(), Module = module, IsCurrent = main.Selected == module && main.SelectedNav is not SubLink },
        SubLink link => new MenuEntry(link, link.Title, link.Icon) { IsCurrent = ReferenceEquals(main.SelectedNav, link) },
        NavHeading { IsGap: false } heading => new MenuEntry(heading, heading.Title, ""),
        _ => null,
    };

    // An entry as a group's short list shows it: no figures, only what it is and whether it is on screen.
    private MenuEntry? Plain(object item, IReadOnlyList<string> dock) => item switch
    {
        Module { InPlanner: false } module => new MenuEntry(module, module.Title, module.Icon)
            { PageKey = module.Key.ToString(), Module = module, IsCurrent = main.Selected == module, OnDock = dock.Contains(module.Key.ToString()) },
        ToolLink tool => new MenuEntry(tool, tool.Title, tool.Icon) { PageKey = DockKeys.Tool(tool.Kind.ToString()) },
        SectionLink section => new MenuEntry(section, section.Title, section.Icon) { IsCurrent = main.Selected.InPlanner },
        SubLink link => new MenuEntry(link, link.Title, link.Icon) { IsCurrent = ReferenceEquals(main.SelectedNav, link) },
        _ => null,
    };

    private void FillMenu()
    {
        // Read when the menu opens, so what it says is as things stand now.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var figures = MenuFigures.For(new MenuFacts
        {
            Today = today,
            Documents = services.Documents.List(),
            UnreadMail = main[Page.Mail].Badge,
            Renewals = services.Store.ListRenewals(),
            Retainers = services.Store.ListRetainers(),
            Drafted = services.Store.DraftsMade().Keys.ToHashSet(),
            ReceivedThisMonth = Webignite.Core.Finance.Ledger.Sum(services.Store.ListEntries(Webignite.Core.Dates.MonthStart(today), today), Webignite.Core.Finance.EntryKind.Received),
            Clients = services.Clients.List(),
        });
        var dock = DockNow();
        MenuColumns.Clear();
        var column = new MenuColumn("Start");
        // Every group shows, whether it is folded in the sidebar or not: there is nowhere to unfold one here. While
        // editing, only what can be taken off or moved is listed, so not the links that come and go with a page.
        foreach (var item in IsMenuEditing ? main.Sidebar.OnMenus() : main.Sidebar.Unfolded())
        {
            if (item is NavGroup group)
            {
                Keep(column);
                // The space above Settings has no name; what follows it is about the app itself.
                column = new MenuColumn(group.CanFold ? group.Name : "Webignite");
                // A named group can go on the dock whole, where it opens a short list of what is in it.
                if (group.CanFold && IsDockMenu && !IsMenuEditing)
                {
                    var groupKey = DockKeys.Group(group.Key);
                    column.Heading = new MenuEntry(group, group.Name, group.Icon)
                    {
                        PageKey = groupKey, OnDock = dock.Contains(groupKey),
                        CanAdd = DockPages.CanAdd(dock, groupKey), CanTakeOff = DockPages.CanRemove(dock, groupKey),
                    };
                }
                continue;
            }
            // Planner's own pages have a column of their own, below.
            if (item is not Module { InPlanner: true } && Entry(item) is { } entry)
                column.Entries.Add(entry);
        }
        Keep(column);
        // Planner's own pages and projects, which the sidebar shows in place of the main list.
        var planner = new MenuColumn("Planner");
        // Planner can go on the dock whole as well, opening a short list of its pages.
        if (IsDockMenu && !IsMenuEditing)
        {
            var plannerKey = DockKeys.Group(PlannerGroup.Key);
            planner.Heading = new MenuEntry(PlannerGroup, PlannerGroup.Name, PlannerGroup.Icon)
            {
                PageKey = plannerKey, OnDock = dock.Contains(plannerKey),
                CanAdd = DockPages.CanAdd(dock, plannerKey), CanTakeOff = DockPages.CanRemove(dock, plannerKey),
            };
        }
        foreach (var item in main.Sidebar.PlannerItems)
            if (item is Module || (item is SubLink && !IsMenuEditing))
                if (Entry(item) is { } entry)
                    planner.Entries.Add(entry);
        Keep(planner);
        // What has been taken off, waiting to be put back.
        MenuAvailable.Clear();
        if (IsMenuEditing)
            foreach (var item in main.Sidebar.TakenOff())
                if (Entry(item) is { } entry)
                    MenuAvailable.Add(entry);
        OnPropertyChanged(nameof(NothingAvailable));

        void Keep(MenuColumn made)
        {
            if (made.Entries.Count > 0)
                MenuColumns.Add(made);
        }

        MenuEntry? Entry(object item) => Made(item) is { } made
            ? made with { Editing = IsMenuEditing, IsOff = main.Sidebar.IsOff(item), CanTakeOffMenu = main.Sidebar.CanTakeOff(item) }
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
                        IsCurrent = main.Selected == module,
                        Module = module,
                        OnDock = dock.Contains(key),
                        CanAdd = IsDockMenu && DockPages.CanAdd(dock, key),
                        CanTakeOff = IsDockMenu && DockPages.CanRemove(dock, key),
                    };
                case ToolLink tool:
                    var toolKey = DockKeys.Tool(tool.Kind.ToString());
                    return new MenuEntry(tool, tool.Title, tool.Icon)
                    {
                        PageKey = toolKey,
                        OnDock = dock.Contains(toolKey),
                        CanAdd = IsDockMenu && DockPages.CanAdd(dock, toolKey),
                        CanTakeOff = IsDockMenu && DockPages.CanRemove(dock, toolKey),
                    };
                case SectionLink section:
                    return new MenuEntry(section, section.Title, section.Icon) { IsCurrent = main.Selected.InPlanner };
                case SubLink link:
                    return new MenuEntry(link, link.Title, link.Icon) { IsCurrent = ReferenceEquals(main.SelectedNav, link) };
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

    /// <summary>The group itself, when it can go on the dock whole; null for the columns that aren't a group.</summary>
    public MenuEntry? Heading { get; set; }
    public bool CanGoOnDock => Heading is not null;

    /// <summary>The group isn't on the dock and the dock has no room left.</summary>
    public bool DockIsFull => Heading is { OnDock: false, CanAdd: false };

    public string? HeadingTip => Heading is null ? null : Heading.OnDock ? "On the dock. Right-click to take it off." : "Right-click to put this whole group on the dock.";
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

    /// <summary>
    /// What it goes by on the dock: a page's own name, or a tool's or a group's with what it is in front
    /// (<see cref="Webignite.Core.Layout.DockKeys"/>). Null for what can't go on the dock (a project, one of My pages).
    /// </summary>
    public string? PageKey { get; init; }

    /// <summary>For a group on the dock: what is in it, shown in a short list above the dock when it is clicked.</summary>
    public IReadOnlyList<MenuEntry> Inside { get; init; } = [];
    public bool IsGroup => Item is NavGroup;

    /// <summary>In a group's list: a small label over the entries that follow ("Work", "Plan"), not something to open.</summary>
    public bool IsSection => Item is NavHeading;

    /// <summary>In a group's list: a link under a page (a project under Projects), set in a little.</summary>
    public bool IsLink => Item is SubLink;
    public bool IsNotGroup => !IsGroup;

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
