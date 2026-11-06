using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One entry in the sidebar. The app is a shell of modules, so Mail and later tools slot in as another entry
/// without touching the rest. What is open is a <see cref="PageHost"/>: each module has its main one, and
/// pages that can be open more than once make more with <see cref="NewHost"/>.
/// </summary>
public sealed partial class Module : ObservableObject
{
    private readonly Func<ViewModelBase> build;

    public Module(Page key, string title, Func<ViewModelBase> build, string icon, bool fullBleed = false, bool readingWidth = false)
    {
        ReadingWidth = readingWidth;
        Key = key;
        Title = title;
        Icon = icon;
        FullBleed = fullBleed;
        this.build = build;
        Main = new PageHost(this, build);
    }

    /// <summary>Which page this is. Jumps from other pages use this, so a title can change freely.</summary>
    public Page Key { get; }

    public string Title { get; }

    /// <summary>The key of its line icon in Styles/Icons.axaml, without the "Icon." in front.</summary>
    public string Icon { get; }

    public bool FullBleed { get; }

    /// <summary>
    /// A page of the Planner section (Home, My tasks and projects, Calendar, Week, Time): it shows in the Planner
    /// sidebar, not the main one, and opening it brings the Planner sidebar up.
    /// </summary>
    public bool InPlanner { get; init; }

    /// <summary>
    /// Forms to read and fill in (Settings) keep a comfortable width on a big screen; everything else uses the
    /// whole window, as lists, boards and calendars should.
    /// </summary>
    public bool ReadingWidth { get; }

    /// <summary>The module's own open copy, the one the sidebar shows.</summary>
    public PageHost Main { get; }

    /// <summary>Another copy of the page, open alongside the first.</summary>
    public PageHost NewHost() => new(this, build);

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
