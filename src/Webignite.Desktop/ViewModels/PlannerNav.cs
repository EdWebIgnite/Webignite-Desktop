using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// An entry in the main sidebar that opens a section with a sidebar of its own (Planner): clicking it swaps the
/// sidebar for the section's menu and opens the section's first page. It moves like any other entry.
/// </summary>
public sealed partial class SectionLink(string key, string title, string icon) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Icon { get; } = icon;

    public NavGroup? Group { get; internal set; }

    internal SidebarViewModel? Sidebar { get; set; }

    public bool CanMoveUp => Sidebar?.CanMove(this, -1) == true;
    public bool CanMoveDown => Sidebar?.CanMove(this, +1) == true;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Sidebar?.Move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Sidebar?.Move(this, +1);

    public IRelayCommand? ResetSidebarCommand => Sidebar?.ResetCommand;

    /// <summary>It can be taken off the menus (everything but Settings and Planner's Home can).</summary>
    public bool CanTakeOffMenu => Sidebar?.CanTakeOff(this) == true;

    [RelayCommand(CanExecute = nameof(CanTakeOffMenu))]
    private void TakeOffMenu() => Sidebar?.TakeOff(this);

    /// <summary>Opens the menu to choose what is on it.</summary>
    public IRelayCommand? EditMenuCommand => Sidebar?.EditCommand;

    internal void Changed()
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(ResetSidebarCommand));
        OnPropertyChanged(nameof(EditMenuCommand));
        OnPropertyChanged(nameof(CanTakeOffMenu));
        TakeOffMenuCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>The top of a section's sidebar: an arrow and the section's name, back to the main sidebar.</summary>
public sealed class SidebarBack(string title)
{
    public string Title { get; } = title;
}

/// <summary>
/// A heading inside a section's sidebar (Work, Plan), with a + when something can be added under it. One with no
/// words is a little space between groups.
/// </summary>
public sealed partial class NavHeading(string title, string addTip = "") : ObservableObject
{
    public string Title { get; } = title;
    public string AddTip { get; } = addTip;
    public bool IsGap => Title.Length == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdd))]
    public partial ICommand? AddCommand { get; set; }

    public bool HasAdd => AddCommand is not null;
}
