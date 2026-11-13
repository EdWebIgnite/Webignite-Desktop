using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The list of keyboard shortcuts with its search box, as F1 and Settings both show it: under headings, the open
/// page's first, narrowed as you type (rules in <see cref="ShortcutList"/>).
/// </summary>
public sealed partial class ShortcutsPart(Func<IReadOnlyList<Shortcut>> all, Func<string?>? first = null) : ObservableObject
{
    public ObservableCollection<ShortcutGroup> Groups { get; } = [];

    [ObservableProperty] public partial string Search { get; set; } = "";

    /// <summary>"Nothing found for …" when the search box leaves the list empty.</summary>
    [ObservableProperty] public partial string Nothing { get; set; } = "";

    partial void OnSearchChanged(string value) => Show();

    /// <summary>Fills the list afresh, with nothing typed.</summary>
    public void Open()
    {
        if (Search.Length > 0)
            Search = "";
        else
            Show();
    }

    /// <summary>Lets the list go, for while it isn't showing.</summary>
    public void Close()
    {
        Groups.Clear();
        Nothing = "";
    }

    private void Show()
    {
        Groups.Clear();
        foreach (var group in ShortcutList.Show(all(), Search, first?.Invoke()))
            Groups.Add(group);
        Nothing = Groups.Count == 0 ? $"No shortcut found for \"{Search.Trim()}\"." : "";
    }
}
