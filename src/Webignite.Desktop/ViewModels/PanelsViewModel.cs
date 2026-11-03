using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The panes of one page: show or hide each, move it left or right, remember a dragged width, and put it
/// all back. The layout is saved under the page's name as soon as it changes.
/// </summary>
public sealed partial class PanelsViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly string page;
    private readonly string[] keys;
    private readonly string? spine;

    /// <param name="spine">A pane that can't be hidden, because the page makes no sense without it.</param>
    public PanelsViewModel(AppSettings settings, string page, IReadOnlyList<(string Key, string Title)> panes, string? spine = null)
    {
        this.settings = settings;
        this.page = page;
        this.spine = spine;
        keys = panes.Select(p => p.Key).ToArray();
        Layout = Panels.Reconcile(settings.PanelLayout(page), keys);
        Panes = panes.Select(p => new PaneViewModel(this, p.Key, p.Title)).ToList();
    }

    [ObservableProperty] public partial PanelLayout Layout { get; private set; }

    public IReadOnlyList<PaneViewModel> Panes { get; }

    public PaneViewModel this[string key] => Panes.Single(p => p.Key == key);

    public bool CanHide(string key) => key != spine && Panels.CanHide(Layout, key);

    public void Show(string key, bool show)
    {
        if (!show && !CanHide(key))
            return;
        Change(Panels.Show(Layout, key, show));
    }

    public void Move(string key, int by) => Change(Panels.Move(Layout, key, by));

    /// <summary>From the view, after a drag.</summary>
    public void Resized(string key, double width) => Change(Panels.Resize(Layout, key, width));

    [RelayCommand]
    private void Reset() => Change(Panels.Default(keys));

    private void Change(PanelLayout layout)
    {
        if (layout == Layout)
            return;
        Layout = layout;
        settings.SetPanelLayout(page, layout);
        foreach (var pane in Panes)
            pane.Changed();
    }
}

/// <summary>One pane in the Panels menu.</summary>
public sealed partial class PaneViewModel(PanelsViewModel owner, string key, string title) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;

    /// <summary>Bound to the tick in the menu. Setting it shows or hides the pane.</summary>
    public bool IsShown
    {
        get => owner.Layout.IsShown(Key);
        set => owner.Show(Key, value);
    }

    public bool CanHide => owner.CanHide(Key);
    public bool CanMoveLeft => Panels.CanMove(owner.Layout, Key, -1);
    public bool CanMoveRight => Panels.CanMove(owner.Layout, Key, +1);

    [RelayCommand(CanExecute = nameof(CanMoveLeft))]
    private void MoveLeft() => owner.Move(Key, -1);

    [RelayCommand(CanExecute = nameof(CanMoveRight))]
    private void MoveRight() => owner.Move(Key, +1);

    internal void Changed()
    {
        OnPropertyChanged(nameof(IsShown));
        OnPropertyChanged(nameof(CanHide));
        OnPropertyChanged(nameof(CanMoveLeft));
        OnPropertyChanged(nameof(CanMoveRight));
        MoveLeftCommand.NotifyCanExecuteChanged();
        MoveRightCommand.NotifyCanExecuteChanged();
    }
}
