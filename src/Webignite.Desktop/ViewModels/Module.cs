using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One entry in the sidebar. The app is a shell of modules, so Mail and later tools slot in as
/// another entry without touching the rest.
/// </summary>
public sealed partial class Module(Page key, string title, ViewModelBase page, string icon, bool isLater = false, bool fullBleed = false) : ObservableObject
{
    /// <summary>Which page this is. Jumps from other pages use this, so a title can change freely.</summary>
    public Page Key { get; } = key;

    public string Title { get; } = title;

    /// <summary>The key of its line icon in Styles/Icons.axaml, without the "Icon." in front.</summary>
    public string Icon { get; } = icon;
    public ViewModelBase Page { get; } = page;
    public bool IsLater { get; } = isLater;
    public bool FullBleed { get; } = fullBleed;

    /// <summary>The heading this page sits under, if any. Folding the group takes the page off the list.</summary>
    public NavGroup? Group { get; internal set; }

    internal SidebarViewModel? Sidebar { get; set; }

    public bool CanMoveUp => Sidebar?.CanMove(this, -1) == true;
    public bool CanMoveDown => Sidebar?.CanMove(this, +1) == true;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Sidebar?.Move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Sidebar?.Move(this, +1);

    /// <summary>The sidebar's right-click menu: put everything back.</summary>
    public IRelayCommand? ResetSidebarCommand => Sidebar?.ResetCommand;

    internal void Changed()
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(ResetSidebarCommand));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A count shown beside the name, e.g. unread mail. Zero hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    public partial int Badge { get; set; }

    public bool HasBadge => Badge > 0;

    /// <summary>Most pages scroll inside a centred column; Mail fills the window with its own panes.</summary>
    public bool Scrolls => !FullBleed;

    // Each page is handed to only one of the two hosts, so its view is never built twice.
    public ViewModelBase? ScrollingPage => FullBleed ? null : Page;
    public ViewModelBase? FullPage => FullBleed ? Page : null;
}

/// <summary>
/// A heading between groups of pages in the sidebar. Clicking it folds the group up to the heading or opens
/// it out again. A heading with no words is only a little space, e.g. above Settings, and can't fold.
/// </summary>
public sealed partial class NavGroup(string key, string title) : ObservableObject
{
    /// <summary>What the folded state is saved under, so the title can change freely.</summary>
    public string Key { get; } = key;
    public string Title { get; } = title;
    public bool CanFold => Title.Length > 0;

    internal SidebarViewModel? Owner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolded))]
    public partial bool IsOpen { get; set; } = true;

    public bool IsFolded => !IsOpen;

    public void Toggle()
    {
        if (CanFold)
            Owner?.Fold(this, !IsOpen);
    }

    [RelayCommand]
    private void ToggleFold() => Toggle();

    public bool CanMoveUp => Owner?.CanMove(this, -1) == true;
    public bool CanMoveDown => Owner?.CanMove(this, +1) == true;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Owner?.Move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Owner?.Move(this, +1);

    public IRelayCommand? ResetSidebarCommand => Owner?.ResetCommand;

    public string FoldText => IsOpen ? "Fold up" : "Open out";

    internal void Changed()
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(ResetSidebarCommand));
        OnPropertyChanged(nameof(FoldText));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}
