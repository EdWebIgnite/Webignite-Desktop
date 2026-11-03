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

    /// <param name="all">Every entry in the app's own order: pages and the group headings between them.</param>
    public SidebarViewModel(AppSettings settings, IReadOnlyList<object> all)
    {
        this.settings = settings;
        byKey = all.ToDictionary(Key);
        defaults = [.. all.Select(Key)];
        var groups = all.OfType<NavGroup>().Where(g => g.CanFold).ToList();
        layout = Sidebar.Reconcile(settings.Sidebar, [.. groups.Select(g => g.Key)]);
        IsHidden = layout.Hidden;
        foreach (var item in all)
        {
            switch (item)
            {
                case NavGroup group: group.Owner = this; break;
                case Module module: module.Sidebar = this; break;
            }
        }
        Items = [];
        Pages = [];
        Apply();
    }

    private static string Key(object item) => item switch
    {
        NavGroup group => Sidebar.Heading(group.Key),
        Module module => module.Key.ToString(),
        _ => throw new ArgumentException("Not a sidebar entry", nameof(item)),
    };

    /// <summary>True when the sidebar is tucked away to a thin strip. Ctrl+B or the button at the top switches it.</summary>
    [ObservableProperty] public partial bool IsHidden { get; private set; }

    /// <summary>What the list shows now: headings, and the pages of groups that are open.</summary>
    public ObservableCollection<object> Items { get; }

    /// <summary>Every page in the order the sidebar has them, folded or not. Ctrl+1 to Ctrl+9 count down this.</summary>
    public List<Module> Pages { get; }

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
        IsHidden = layout.Hidden;
        Apply();
    }

    private void Apply()
    {
        var order = Sidebar.Resolve(layout, defaults);
        var wanted = new List<object>();
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
            else if (item is Module module)
            {
                module.Group = group;
                Pages.Add(module);
                if (group is null || !group.CanFold || group.IsOpen)
                    wanted.Add(module);
            }
        }
        Rebuild(wanted);
        foreach (var item in byKey.Values)
        {
            if (item is Module module)
                module.Changed();
            else if (item is NavGroup heading)
                heading.Changed();
        }
    }

    // Change the list in place so the ListBox keeps its scroll and the page stays selected.
    private void Rebuild(List<object> wanted)
    {
        if (wanted.SequenceEqual(Items))
            return;
        for (var i = Items.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Items[i]))
                Items.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = Items.IndexOf(wanted[i]);
            if (at == i)
                continue;
            if (at >= 0)
                Items.Move(at, i);
            else
                Items.Insert(i, wanted[i]);
        }
    }
}
