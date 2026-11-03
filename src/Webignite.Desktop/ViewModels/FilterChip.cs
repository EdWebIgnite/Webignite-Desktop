using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>One quick filter pill above a list, e.g. Overdue. Only one in a row is on at a time.</summary>
public sealed partial class FilterChip(string label, Action pick) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [RelayCommand]
    private void Pick() => pick();
}
