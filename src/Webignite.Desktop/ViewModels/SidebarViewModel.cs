using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The sidebar: tuck it away or bring it back, and fold each group of pages up to its heading.
/// <see cref="Items"/> is what the list shows; pages in a folded group are left out of it. How it was
/// left is saved straight away.
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly IReadOnlyList<object> all;
    private SidebarLayout layout;

    /// <param name="all">Every entry in order: pages and the group headings between them.</param>
    public SidebarViewModel(AppSettings settings, IReadOnlyList<object> all)
    {
        this.settings = settings;
        this.all = all;
        var groups = all.OfType<NavGroup>().Where(g => g.CanFold).ToList();
        layout = Sidebar.Reconcile(settings.Sidebar, [.. groups.Select(g => g.Key)]);
        IsHidden = layout.Hidden;
        foreach (var group in groups)
        {
            group.Owner = this;
            group.IsOpen = layout.IsOpen(group.Key);
        }
        Items = [];
        Rebuild();
    }

    /// <summary>True when the sidebar is tucked away to a thin strip. Ctrl+B or the button at the top switches it.</summary>
    [ObservableProperty] public partial bool IsHidden { get; private set; }

    /// <summary>What the list shows now: headings, and the pages of groups that are open.</summary>
    public ObservableCollection<object> Items { get; }

    [RelayCommand]
    public void Toggle() => Change(Sidebar.Toggle(layout));

    internal void Fold(NavGroup group, bool open) => Change(Sidebar.Fold(layout, group.Key, open));

    private void Change(SidebarLayout changed)
    {
        if (changed == layout)
            return;
        layout = changed;
        settings.Sidebar = layout;
        IsHidden = layout.Hidden;
        foreach (var group in all.OfType<NavGroup>())
            group.IsOpen = layout.IsOpen(group.Key);
        Rebuild();
    }

    private void Rebuild()
    {
        var wanted = all.Where(item => item is not Module { Group: { CanFold: true, IsOpen: false } }).ToList();
        if (wanted.SequenceEqual(Items))
            return;
        // Change the list in place so the ListBox keeps its scroll and the page stays selected.
        for (var i = Items.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Items[i]))
                Items.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
            if (i >= Items.Count || Items[i] != wanted[i])
                Items.Insert(i, wanted[i]);
    }
}
