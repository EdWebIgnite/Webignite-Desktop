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

    /// <summary>Every page and tool for the icon strip, folded groups or not.</summary>
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

    private void Change(SidebarLayout changed)
    {
        if (changed == layout)
            return;
        layout = changed;
        settings.Sidebar = layout;
        ShowMode(layout.Mode);
        Apply();
    }

    private void Apply()
    {
        var order = Sidebar.Resolve(layout, defaults);
        var wanted = new List<object>();
        var icons = new List<object>();
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
                icons.Add(section);
                if (group is null || !group.CanFold || group.IsOpen)
                    wanted.Add(section);
            }
            else if (item is Module module)
            {
                module.Group = group;
                Pages.Add(module);
                icons.Add(module);
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
                icons.Add(tool);
                if (group is null || !group.CanFold || group.IsOpen)
                    wanted.Add(tool);
            }
        }
        Rebuild(Items, wanted);
        mainIcons = icons;
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

    // The Planner sidebar, built from the Planner pages in the app's order and the links that go with Projects.
    private void BuildPlanner(List<string> order)
    {
        var pages = order.Select(k => byKey[k]).OfType<Module>().Where(m => m.InPlanner).ToList();
        var home = pages.FirstOrDefault(m => m.Key == Page.Home);
        var projects = pages.FirstOrDefault(m => m.Key == Page.Projects);
        var (above, below) = links.GetValueOrDefault(Page.Projects, ([], []));
        var wanted = new List<object> { PlannerBack };
        if (home is not null)
            wanted.Add(home);
        wanted.Add(plannerGap);
        wanted.AddRange(above);
        if (projects is not null)
            wanted.Add(projects);
        wanted.Add(WorkHeading);
        wanted.AddRange(below);
        wanted.Add(planHeading);
        wanted.AddRange(pages.Where(m => m != home && m != projects));
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
