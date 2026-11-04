using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Side by side: a second page beside the one on screen, in the same window. Drag a tab to the left or right
/// edge of the page, or right-click it and choose a side. The sidebar, tabs and jumps keep working in the main
/// side; the page beside has its own little header to swap sides, go back to being a tab, or close.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The page shown beside the main one, or null when the window isn't split.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplit))]
    public partial PageHost? Beside { get; private set; }

    [ObservableProperty] public partial bool BesideOnLeft { get; private set; }

    /// <summary>How much of the width the main page takes when split.</summary>
    [ObservableProperty] public partial double MainShare { get; set; } = 0.5;

    public bool IsSplit => Beside is not null;

    partial void OnMainShareChanged(double value)
    {
        var kept = TabRules.Share(value);
        if (kept != value)
            MainShare = kept;
    }

    /// <summary>Whether a page is shown somewhere other than the main side: popped out, or beside.</summary>
    private bool IsElsewhere(PageHost host) => PoppedOut.Contains(host) || host == Beside;

    /// <summary>
    /// Shows a tab beside the main page. A page already beside goes back to being a tab first. The last tab can't
    /// go: a fresh Overview takes its place on the main side.
    /// </summary>
    public void ShowBeside(PageHost host, bool onLeft)
    {
        if (host == Beside)
        {
            BesideOnLeft = onLeft;
            return;
        }
        if (Beside is not null)
            PutBesideBack();
        if (Tabs.Contains(host))
        {
            if (Tabs.Count == 1)
                OpenInNewTab(this[Page.Overview]);
            if (Current == host)
            {
                var at = Tabs.IndexOf(host);
                Current = Tabs.Where(t => t != host).ElementAt(TabRules.AfterClose(at, Tabs.Count - 1));
            }
            Tabs.Remove(host);
            OnPropertyChanged(nameof(ShowTabs));
        }
        if (!Open.Contains(host))
            Open.Add(host);
        BesideOnLeft = onLeft;
        Beside = host;
        host.LastSeen = DateTimeOffset.Now;
    }

    [RelayCommand]
    private void ShowOnRight(PageHost? tab) => ShowBeside(tab ?? Current, onLeft: false);

    [RelayCommand]
    private void ShowOnLeft(PageHost? tab) => ShowBeside(tab ?? Current, onLeft: true);

    [RelayCommand]
    private void SwapSides() => BesideOnLeft = !BesideOnLeft;

    /// <summary>The page beside goes back to being a tab, next to the one on screen, and the window is whole again.</summary>
    [RelayCommand]
    public void PutBesideBack()
    {
        if (Beside is not { } host)
            return;
        Beside = null;
        Tabs.Insert(Tabs.IndexOf(Current) + 1, host);
        OnPropertyChanged(nameof(ShowTabs));
    }

    /// <summary>Closes the page beside. One with work open goes back to being a tab instead, so nothing is lost.</summary>
    [RelayCommand]
    public void CloseBeside()
    {
        if (Beside is not { } host)
            return;
        if (host.IsBuilt && host.Page is IHoldsWork { HoldsWork: true })
        {
            PutBesideBack();
            return;
        }
        Beside = null;
        if (!host.IsMain)
            Close(host, quietly: true);
    }

    // Saved with the tabs; put back after them.
    private void RestoreBeside(TabSettings back)
    {
        MainShare = back.MainShare;
        if (back.Beside is not { } name)
            return;
        var module = this[Enum.Parse<Page>(name)];
        var host = !Tabs.Contains(module.Main) && !PoppedOut.Contains(module.Main) ? module.Main : module.NewHost();
        if (!Open.Contains(host))
            Open.Add(host);
        BesideOnLeft = back.BesideOnLeft;
        Beside = host;
    }
}
