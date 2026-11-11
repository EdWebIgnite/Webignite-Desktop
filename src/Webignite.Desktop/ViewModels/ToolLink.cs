using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>The small tools, each opened in a window of its own from the sidebar.</summary>
public enum ToolKind
{
    Calculator,
    Passwords,
    Colours,
    Clipboard,
    Qr,
    Images,
    WebHelpers,
    Dns,
    PageCapture,
    Ruler,
}

/// <summary>
/// A tool in the sidebar. Unlike a page it doesn't take over the window: clicking it opens the tool in a small
/// window beside whatever is open, or brings that window forward when it is open already.
/// </summary>
public sealed partial class ToolLink(ToolKind kind, string title, string icon) : ObservableObject
{
    public ToolKind Kind { get; } = kind;
    public string Title { get; } = title;

    /// <summary>The key of its line icon in Styles/Icons.axaml, without the "Icon." in front.</summary>
    public string Icon { get; } = icon;

    /// <summary>The heading it sits under, if any. Folding the group takes it off the list.</summary>
    public NavGroup? Group { get; internal set; }

    internal SidebarViewModel? Sidebar { get; set; }

    public bool CanMoveUp => Sidebar?.CanMove(this, -1) == true;
    public bool CanMoveDown => Sidebar?.CanMove(this, +1) == true;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Sidebar?.Move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Sidebar?.Move(this, +1);

    public IRelayCommand? ResetSidebarCommand => Sidebar?.ResetCommand;

    internal void Changed()
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(ResetSidebarCommand));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}
