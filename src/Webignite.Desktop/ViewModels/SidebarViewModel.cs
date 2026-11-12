using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The sidebar: tuck it away or bring it back, fold each group of pages up to its heading, and move
/// pages and groups up or down. <see cref="Items"/> is what the list shows; pages in a folded group are
/// left out of it. How it was left is saved straight away.
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly Dictionary<string, object> byKey;
    private readonly string[] defaults;
    private SidebarLayout layout;
    private readonly Dictionary<Page, (IReadOnlyList<SubLink> Above, IReadOnlyList<SubLink> Below)> links = [];

    /// <param name="all">Every entry in the app's own order: pages and the group headings between them.</param>
    public SidebarViewModel(AppSettings settings, IReadOnlyList<object> all)
    {
        this.settings = settings;
        byKey = all.ToDictionary(Key);
        defaults = [.. all.Select(Key)];
        var groups = all.OfType<NavGroup>().Where(g => g.CanFold).ToList();
        layout = Sidebar.Reconcile(settings.Sidebar, [.. groups.Select(g => g.Key)]);
        ShowMode(layout.Mode);
        foreach (var item in all)
        {
            switch (item)
            {
                case NavGroup group: group.Owner = this; break;
                case Module module: module.Sidebar = this; break;
                case ToolLink tool: tool.Sidebar = this; break;
                case SectionLink section: section.Sidebar = this; break;
            }
        }
        Items = [];
        PlannerItems = [];
        Pages = [];
        Apply();
    }

    private static string Key(object item) => item switch
    {
        NavGroup group => Sidebar.Heading(group.Key),
        Module module => module.Key.ToString(),
        ToolLink tool => "tool-" + tool.Kind,
        SectionLink section => "section-" + section.Key,
        _ => throw new ArgumentException("Not a sidebar entry", nameof(item)),
    };

    /// <summary>True when the sidebar is tucked away to a thin strip. Ctrl+B or the button at the top switches it.</summary>
    [ObservableProperty] public partial bool IsHidden { get; private set; }

    /// <summary>True when the sidebar is folded to a strip of page icons, names on hover.</summary>
    [ObservableProperty] public partial bool IsIcons { get; private set; }

    /// <summary>True when the sidebar shows the names of the pages.</summary>
    [ObservableProperty] public partial bool IsFull { get; private set; } = true;

    /// <summary>What the button at the top does next, for its tip.</summary>
    public string ToggleTip => IsFull ? "Fold the sidebar to icons (Ctrl+B)" : IsIcons ? "Tuck the sidebar away (Ctrl+B)" : "Show the sidebar (Ctrl+B)";

    /// <summary>
    /// The icon strip: pages outside a group as their own icons, and each group as one icon whose pages open beside
    /// it on hover, so the strip stays short. A group with only one page shows that page instead.
    /// </summary>
    public ObservableCollection<object> IconPages { get; } = [];

    private void ShowMode(SidebarMode mode)
    {
        IsHidden = mode == SidebarMode.Hidden;
        IsIcons = mode == SidebarMode.Icons;
        IsFull = mode == SidebarMode.Open;
        OnPropertyChanged(nameof(ToggleTip));
    }

    /// <summary>What the main list shows now: headings, and the pages of groups that are open.</summary>
    public ObservableCollection<object> Items { get; }

    /// <summary>
    /// The Planner sidebar: a way back, Home, My tasks and Projects, each running project under Work, and the
    /// planning pages (To-do, Calendar, Week, Time). It takes the main list's place while a Planner page is open.
    /// </summary>
    public ObservableCollection<object> PlannerItems { get; }

    /// <summary>The list on screen: Planner's while in that section, the main one otherwise.</summary>
    public ObservableCollection<object> Shown => IsPlanner ? PlannerItems : Items;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown))]
    public partial bool IsPlanner { get; private set; }

    public SidebarBack PlannerBack { get; } = new("Planner");
    public NavHeading WorkHeading { get; } = new("WORK", "New project");
    private readonly NavHeading planHeading = new("PLAN");
    private readonly NavHeading plannerGap = new("");
    private List<object> mainIcons = [];
    private List<object> plannerIcons = [];

    /// <summary>Shows the Planner sidebar; a Planner page opening calls it.</summary>
    public void EnterPlanner()
    {
        IsPlanner = true;
        ShowIcons();
    }

    /// <summary>Back to the main sidebar: the back arrow, or another page opening.</summary>
    public void LeavePlanner()
    {
        IsPlanner = false;
        ShowIcons();
    }

    private void ShowIcons()
    {
        var icons = IsPlanner ? plannerIcons : mainIcons;
        if (IconPages.SequenceEqual(icons))
            return;
        IconPages.Clear();
        foreach (var entry in icons)
            IconPages.Add(entry);
    }

    /// <summary>Every page in the order the sidebar has them, folded or not. Ctrl+1 to Ctrl+9 count down this.</summary>
    public List<Module> Pages { get; }

    /// <summary>
    /// Entries that go with a page and open parts of it: for Projects, My tasks above it and each running project
    /// and the templates below; for My pages, each saved page. They move with the page and fold with its group.
    /// </summary>
    public void ShowLinks(Page page, IReadOnlyList<SubLink> above, IReadOnlyList<SubLink> below)
    {
        links[page] = (above, below);
        Apply();
    }

    [RelayCommand]
    public void Toggle() => Change(Sidebar.Toggle(layout));

    /// <summary>The app's own order, every group open.</summary>
    [RelayCommand]
    private void Reset() => Change(Sidebar.Reset(layout));

    internal void Fold(NavGroup group, bool open) => Change(Sidebar.Fold(layout, group.Key, open));

    internal void Move(object item, int by) => Change(Sidebar.Move(layout, defaults, Key(item), by));

    internal bool CanMove(object item, int by) => Sidebar.CanMove(layout, defaults, Key(item), by);

    /// <summary>True for an entry that can be dragged: a page, a group heading, a tool or a section.</summary>
    public bool CanDrag(object item) => item is NavGroup { CanFold: true } or Module or ToolLink or SectionLink && byKey.ContainsKey(Key(item));

    /// <summary>Whether an entry dragged onto another would land just above it, or just below it.</summary>
    public bool CanMoveTo(object item, object target, bool below) =>
        CanDrag(item) && target is NavGroup or Module or ToolLink or SectionLink && byKey.ContainsKey(Key(target))
        && Sidebar.CanMoveTo(layout, defaults, Key(item), Key(target), below);

    /// <summary>Drops a dragged entry just above or below another; a heading takes its pages with it.</summary>
    public void MoveTo(object item, object target, bool below)
    {
        if (CanMoveTo(item, target, below))
            Change(Sidebar.MoveTo(layout, defaults, Key(item), Key(target), below));
    }

    // Choosing what is on the menus. What is taken off is off the sidebar, the Menu button and the dock alike.

    /// <summary>Set by the main window's view model: opens the menu to choose what is on it.</summary>
    public Action? EditAsked { get; set; }

    [RelayCommand]
    private void Edit() => EditAsked?.Invoke();

    /// <summary>Raised when what is on the menus, or its order, changes, so the dock and the open menu follow.</summary>
    public event Action? LayoutChanged;

    public bool CanTakeOff(object item) => item is Module or ToolLink or SectionLink && byKey.ContainsKey(Key(item)) && Sidebar.CanTakeOff(Key(item));

    public bool IsOff(object item) => item is Module or ToolLink or SectionLink && layout.IsOff(Key(item));

    public void TakeOff(object item)
    {
        if (CanTakeOff(item))
            Change(Sidebar.TakeOff(layout, Key(item)));
    }

    public void PutBack(object item)
    {
        if (item is Module or ToolLink or SectionLink)
            Change(Sidebar.PutBack(layout, Key(item)));
    }

    public void PutAllBack() => Change(Sidebar.PutAllBack(layout));

    /// <summary>True when something has been taken off the menus.</summary>
    public bool HasTakenOff => Sidebar.TakenOff(layout, defaults).Count > 0;

    /// <summary>Every entry that is on the menus, headings too, in the person's order, whether its group is folded or not.</summary>
    public IReadOnlyList<object> OnMenus() => [.. Sidebar.Shown(layout, defaults).Select(key => byKey[key])];

    /// <summary>
    /// Everything on the menus as the sidebar lists it with every group open: headings, pages, tools and the links
    /// that sit beside a page (each of My pages). The Menu button's menu and the dock's More show this, as a group
    /// folded in the sidebar has nowhere to be unfolded there.
    /// </summary>
    public IReadOnlyList<object> Unfolded()
    {
        var all = new List<object>();
        foreach (var item in OnMenus())
        {
            if (item is Module { InPlanner: false } module)
            {
                var (above, below) = links.GetValueOrDefault(module.Key, ([], []));
                all.AddRange(above);
                all.Add(module);
                all.AddRange(below);
            }
            else
                all.Add(item);
        }
        return all;
    }

    /// <summary>What has been taken off, in the app's own order.</summary>
    public IReadOnlyList<object> TakenOff() => [.. Sidebar.TakenOff(layout, defaults).Select(key => byKey[key])];

    private void Change(SidebarLayout changed)
    {
        if (changed == layout)
            return;
        layout = changed;
        settings.Sidebar = layout;
        ShowMode(layout.Mode);
        Apply();
        LayoutChanged?.Invoke();
    }

    private void Apply()
    {
        // Only what is on the menus: entries taken off are left out, and a group left with nothing goes with them.
        var order = Sidebar.Shown(layout, defaults);
        var wanted = new List<object>();
        var icons = new List<object>();
        var entries = new Dictionary<NavGroup, List<GroupEntry>>();
        Pages.Clear();
        NavGroup? group = null;
        foreach (var key in order)
        {
            var item = byKey[key];
            if (item is NavGroup heading)
            {
                group = heading;
                heading.IsOpen = layout.IsOpen(heading.Key);
                wanted.Add(heading);
            }
            else if (item is Module { InPlanner: true } planned)
            {
                planned.Group = group;
                Pages.Add(planned);
            }
            else if (item is SectionLink section)
            {
                section.Group = group;
                Strip(icons, entries, group, section, new GroupEntry(section.Title, section.Icon, section));
                if (group is null || !group.CanFold || group.IsOpen)
                    wanted.Add(section);
            }
            else if (item is Module module)
            {
                module.Group = group;
                Pages.Add(module);
                var (aboveLinks, belowLinks) = links.GetValueOrDefault(module.Key, ([], []));
                Strip(icons, entries, group, module, new GroupEntry(module.Title, module.Icon, module),
                    [.. aboveLinks.Concat(belowLinks).Select(l => new GroupEntry(l.Title, l.Icon, l))]);
                if (group is null || !group.CanFold || group.IsOpen)
                {
                    var (above, below) = links.GetValueOrDefault(module.Key, ([], []));
                    wanted.AddRange(above);
                    wanted.Add(module);
                    wanted.AddRange(below);
                }
            }
            else if (item is ToolLink tool)
            {
                tool.Group = group;
                Strip(icons, entries, group, tool, new GroupEntry(tool.Title, tool.Icon, tool));
                if (group is null || !group.CanFold || group.IsOpen)
                    wanted.Add(tool);
            }
        }
        Rebuild(Items, wanted);
        mainIcons = FoldGroups(icons, entries);
        BuildPlanner(order);
        ShowIcons();
        foreach (var item in byKey.Values)
        {
            if (item is Module module)
                module.Changed();
            else if (item is ToolLink tool)
                tool.Changed();
            else if (item is SectionLink section)
                section.Changed();
            else if (item is NavGroup heading)
                heading.Changed();
        }
    }

    // An entry for the icon strip: in a group, it goes in the group's list and the group's icon holds its place.
    private static void Strip(List<object> icons, Dictionary<NavGroup, List<GroupEntry>> entries, NavGroup? group, object item,
        GroupEntry entry, IReadOnlyList<GroupEntry>? extra = null)
    {
        if (group is not { CanFold: true })
        {
            icons.Add(item);
            return;
        }
        if (!entries.TryGetValue(group, out var list))
        {
            entries[group] = list = [];
            icons.Add(group);
        }
        list.Add(entry);
        list.AddRange(extra ?? []);
    }

    // Each group's list filled in; a group holding a single page shows that page's own icon instead.
    private static List<object> FoldGroups(List<object> icons, Dictionary<NavGroup, List<GroupEntry>> entries)
    {
        var strip = new List<object>();
        foreach (var icon in icons)
        {
            if (icon is not NavGroup group)
            {
                strip.Add(icon);
                continue;
            }
            var list = entries[group];
            if (list.Count == 1)
            {
                strip.Add(list[0].Target);
                continue;
            }
            if (!group.Entries.SequenceEqual(list))
            {
                group.Entries.Clear();
                foreach (var entry in list)
                    group.Entries.Add(entry);
            }
            strip.Add(group);
        }
        return strip;
    }

    // The Planner sidebar, built from the Planner pages in the app's order and the links that go with Projects.
    private void BuildPlanner(List<string> order)
    {
        var pages = order.Select(k => byKey[k]).OfType<Module>().Where(m => m.InPlanner).ToList();
        var home = pages.FirstOrDefault(m => m.Key == Page.Home);
        var inbox = pages.FirstOrDefault(m => m.Key == Page.Inbox);
        var projects = pages.FirstOrDefault(m => m.Key == Page.Projects);
        var (above, below) = links.GetValueOrDefault(Page.Projects, ([], []));
        var wanted = new List<object> { PlannerBack };
        if (home is not null)
            wanted.Add(home);
        // Inbox sits with Home at the top, as Asana's.
        if (inbox is not null)
            wanted.Add(inbox);
        wanted.Add(plannerGap);
        wanted.AddRange(above);
        if (projects is not null)
            wanted.Add(projects);
        wanted.Add(WorkHeading);
        wanted.AddRange(below);
        wanted.Add(planHeading);
        wanted.AddRange(pages.Where(m => m != home && m != inbox && m != projects));
        Rebuild(PlannerItems, wanted);
        plannerIcons = [PlannerBack, .. pages];
    }

    // Change a list in place so the ListBox keeps its scroll and the page stays selected.
    private static void Rebuild(ObservableCollection<object> list, List<object> wanted)
    {
        if (wanted.SequenceEqual(list))
            return;
        for (var i = list.Count - 1; i >= 0; i--)
            if (!wanted.Contains(list[i]))
                list.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = list.IndexOf(wanted[i]);
            if (at == i)
                continue;
            if (at >= 0)
                list.Move(at, i);
            else
                list.Insert(i, wanted[i]);
        }
    }
}
