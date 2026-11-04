using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Tabs across the whole app, like a browser. Clicking in the sidebar changes what the tab on screen shows
/// (or goes to a tab that already shows that page); Ctrl+click or a middle click opens it in a new tab.
/// Ctrl+T, Ctrl+W and Ctrl+Tab do what they do in a browser. The strip shows once there are two tabs.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The tabs, left to right. The one on screen is <see cref="Current"/>.</summary>
    public ObservableCollection<PageHost> Tabs { get; } = [];

    public bool ShowTabs => Tabs.Count > 1;

    private PageHost? leaving;

    // Whatever comes on screen takes the place of what the tab showed, unless it already has a tab of its own.
    private void KeepInTabs(PageHost showing)
    {
        if (Tabs.Contains(showing))
            return;
        var slot = leaving is null ? -1 : Tabs.IndexOf(leaving);
        if (slot < 0)
        {
            Tabs.Add(showing);
        }
        else
        {
            var old = leaving!;
            Tabs[slot] = showing;
            // An extra copy no tab shows any more is closed, unless it holds work: then it waits, and the sidebar
            // goes back to it.
            if (!old.IsMain)
                Close(old, quietly: true);
        }
        OnPropertyChanged(nameof(ShowTabs));
    }

    /// <summary>The tab that shows a page already, the one looked at last.</summary>
    private PageHost? TabFor(Module module) => Tabs.Where(t => t.Module == module).MaxBy(t => t.LastSeen);

    /// <summary>Ctrl+click or middle click in the sidebar: the page in a tab of its own, beside the one on screen.</summary>
    public PageHost OpenInNewTab(Module module)
    {
        var host = Tabs.Contains(module.Main) ? module.NewHost() : module.Main;
        if (!Open.Contains(host))
            Open.Add(host);
        Tabs.Insert(Tabs.IndexOf(Current) + 1, host);
        OnPropertyChanged(nameof(ShowTabs));
        Current = host;
        return host;
    }

    [RelayCommand]
    private void NewTabFor(Module module) => OpenInNewTab(module);

    [RelayCommand]
    private void CloseThisTab(PageHost? tab) => CloseTab(tab);

    /// <summary>Ctrl+T: a new tab on Overview.</summary>
    [RelayCommand]
    private void NewTab() => OpenInNewTab(this[Page.Overview]);

    /// <summary>
    /// Ctrl+W or the tab's cross. The last tab can't go; a tab with open work (an invoice being edited, a mail
    /// being written) stays until that is saved or put away, with a note saying so.
    /// </summary>
    public bool CloseTab(PageHost? tab)
    {
        tab ??= Current;
        var at = Tabs.IndexOf(tab);
        if (at < 0 || Tabs.Count == 1)
            return false;
        if (!tab.IsMain && tab.IsBuilt && tab.Page is IHoldsWork { HoldsWork: true })
        {
            services.ShowNote?.Invoke("This tab has work open", $"Save or close what's open in {tab.Title} first.", () => Current = tab);
            return false;
        }
        if (tab == Current)
            Current = Tabs.Where(t => t != tab).ElementAt(TabRules.AfterClose(at, Tabs.Count - 1));
        Tabs.Remove(tab);
        if (!tab.IsMain)
            Close(tab, quietly: true);
        OnPropertyChanged(nameof(ShowTabs));
        return true;
    }

    /// <summary>Closes every tab but this one.</summary>
    [RelayCommand]
    private void CloseOtherTabs(PageHost? tab)
    {
        tab ??= Current;
        Current = tab;
        foreach (var other in Tabs.Where(t => t != tab).ToList())
            CloseTab(other);
    }

    /// <summary>Ctrl+Tab and Ctrl+Shift+Tab.</summary>
    [RelayCommand]
    private void NextTab(string by)
    {
        if (Tabs.Count > 1 && int.TryParse(by, out var step))
            Current = Tabs[TabRules.Step(Tabs.IndexOf(Current), Tabs.Count, step)];
    }

    /// <summary>What to save: the tabs by page name, and which was showing.</summary>
    public TabSettings TabState(Func<PageHost, PopOutPlacement?>? where = null) => new()
    {
        Pages = [.. Tabs.Select(t => t.Key.ToString())],
        Active = Math.Max(Tabs.IndexOf(Current), 0),
        PoppedOut = [.. PoppedOut.Select(h => where?.Invoke(h)).OfType<PopOutPlacement>()],
        Beside = Beside?.Key.ToString(),
        BesideOnLeft = BesideOnLeft,
        MainShare = MainShare,
    };

    /// <summary>Puts the tabs back as they were. Only the one showing is built; the rest wait to be clicked.</summary>
    public void RestoreTabs(TabSettings saved)
    {
        var known = Modules.Select(m => m.Key.ToString()).ToHashSet();
        var back = TabRules.Restore(saved, known, nameof(Page.Overview));
        var hosts = new List<PageHost>();
        foreach (var name in back.Pages)
        {
            var module = this[Enum.Parse<Page>(name)];
            var host = hosts.Contains(module.Main) ? module.NewHost() : module.Main;
            if (!Open.Contains(host))
                Open.Add(host);
            hosts.Add(host);
        }
        leaving = null;
        Tabs.Clear();
        foreach (var host in hosts)
            Tabs.Add(host);
        OnPropertyChanged(nameof(ShowTabs));
        Current = hosts[back.Active];
        Beside = null;
        RestoreBeside(back);
    }

    /// <summary>The popped-out pages to open again in windows of their own, each with where it was.</summary>
    public IReadOnlyList<(PageHost Host, PopOutPlacement Where)> RestorePoppedOut(TabSettings saved)
    {
        var known = Modules.Select(m => m.Key.ToString()).ToHashSet();
        var back = new List<(PageHost, PopOutPlacement)>();
        foreach (var place in TabRules.Restore(saved, known, nameof(Page.Overview)).PoppedOut)
        {
            var module = this[Enum.Parse<Page>(place.Page)];
            var host = Open.Contains(module.Main) && !Tabs.Contains(module.Main) && !PoppedOut.Contains(module.Main) ? module.Main : module.NewHost();
            if (PopOut(host))
                back.Add((host, place));
        }
        return back;
    }
}
