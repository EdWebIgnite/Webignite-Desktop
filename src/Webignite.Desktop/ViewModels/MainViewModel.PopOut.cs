using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Pages popped out of the main window into windows of their own, e.g. Mail on one screen and a client on the
/// other. Drag a tab out of the strip, or right-click it and choose Open in new window; drag the window's
/// header back onto the main window's tabs to put it back. The main window makes and closes the windows.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Each popped-out page has a window of its own while it is in here.</summary>
    public ObservableCollection<PageHost> PoppedOut { get; } = [];

    /// <summary>
    /// Takes a tab out into a window of its own. The last tab can't go: a fresh Overview takes its place in the
    /// main window. Returns false when it is already out.
    /// </summary>
    public bool PopOut(PageHost host)
    {
        if (PoppedOut.Contains(host))
            return false;
        if (host == Beside)
            Beside = null;
        if (!Open.Contains(host))
            Open.Add(host);
        if (Tabs.Contains(host))
        {
            if (Tabs.Count == 1)
                OpenInNewTab(this[Page.Overview]);
            if (Current == host)
            {
                var at = Tabs.IndexOf(host);
                Current = Tabs.Where(t => t != host).ElementAt(Core.Layout.TabRules.AfterClose(at, Tabs.Count - 1));
            }
            Tabs.Remove(host);
            OnPropertyChanged(nameof(ShowTabs));
        }
        PoppedOut.Add(host);
        return true;
    }

    [RelayCommand]
    private void PopOutTab(PageHost? tab) => PopOut(tab ?? Current);

    /// <summary>
    /// A page in a window of its own, from the dock or the menu: the copy on screen when it is that page, otherwise
    /// a new copy, so what is on screen stays where it is.
    /// </summary>
    public void OpenInWindow(Module module) => PopOut(Current.Module == module ? Current : OpenInNewTab(module));

    /// <summary>Puts a popped-out page back as a tab in the main window, beside the one on screen.</summary>
    public void DockBack(PageHost host)
    {
        if (!PoppedOut.Remove(host))
            return;
        Tabs.Insert(Tabs.IndexOf(Current) + 1, host);
        OnPropertyChanged(nameof(ShowTabs));
        Current = host;
    }

    /// <summary>
    /// The popped-out window was closed. Its page goes as a tab would; one with work open goes back into the main
    /// window instead, so nothing is lost.
    /// </summary>
    public void PoppedOutClosed(PageHost host)
    {
        if (!PoppedOut.Contains(host))
            return;
        if (host.IsBuilt && host.Page is IHoldsWork { HoldsWork: true })
        {
            DockBack(host);
            return;
        }
        PoppedOut.Remove(host);
        if (!host.IsMain)
            Close(host, quietly: true);
    }

    /// <summary>Whether a page is in a window of its own, so the main window leaves it there.</summary>
    public bool IsPoppedOut(PageHost host) => PoppedOut.Contains(host);
}
