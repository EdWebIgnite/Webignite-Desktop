using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Keeps a list that can grow long to its first few rows, with Show more under it (<see cref="FirstFew"/>). The page
/// binds its list to the collection handed in here, and a link to <see cref="MoreText"/> and <see cref="ToggleCommand"/>.
/// </summary>
public sealed partial class FirstFewList<T>(ObservableCollection<T> shown, int first) : ObservableObject
{
    private IReadOnlyList<T> all = [];
    private bool showAll;

    /// <summary>Everything, shown or not.</summary>
    public IReadOnlyList<T> All => all;

    public string MoreText => FirstFew.MoreText(all.Count, first, showAll);
    public bool HasMore => MoreText.Length > 0;

    /// <summary>The whole list as it is now; what was asked for (the first few, or all) stays as it was.</summary>
    public void Set(IEnumerable<T> items)
    {
        all = [.. items];
        Show();
    }

    [RelayCommand]
    private void Toggle()
    {
        showAll = !showAll;
        Show();
    }

    private void Show()
    {
        shown.Clear();
        foreach (var item in FirstFew.Of(all, first, showAll))
            shown.Add(item);
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(HasMore));
    }
}
