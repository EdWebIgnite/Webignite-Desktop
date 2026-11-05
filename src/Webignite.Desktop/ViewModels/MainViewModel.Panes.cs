using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Webignite.Desktop.ViewModels;

// The page on screen's minimised panes, as chips under its name in the sidebar, to bring them back.
public partial class MainViewModel
{
    private PanelsViewModel? watchedPanes;

    public ObservableCollection<PaneViewModel> HiddenPanes { get; } = [];

    public bool HasHiddenPanes => HiddenPanes.Count > 0;

    /// <summary>Follows the panes of the page now on screen; called whenever it changes.</summary>
    private void WatchPanes(PageHost host)
    {
        if (watchedPanes is not null)
            watchedPanes.PropertyChanged -= PanesChanged;
        watchedPanes = host.Page is IHasPanes page ? page.Panels : null;
        if (watchedPanes is not null)
            watchedPanes.PropertyChanged += PanesChanged;
        ShowHiddenPanes();
    }

    private void PanesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelsViewModel.Hidden))
            ShowHiddenPanes();
    }

    private void ShowHiddenPanes()
    {
        HiddenPanes.Clear();
        foreach (var pane in watchedPanes?.Hidden ?? [])
            HiddenPanes.Add(pane);
        OnPropertyChanged(nameof(HasHiddenPanes));
    }
}
