using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;

namespace Webignite.Desktop.ViewModels;

/// <summary>The watched sites: every client website, plus any added here, visited every 15 minutes.</summary>
public sealed partial class SiteCheckViewModel
{
    public ObservableCollection<WatchedSiteViewModel> Watched { get; } = [];
    public bool HasWatched => Watched.Count > 0;

    [ObservableProperty] public partial string WatchSummary { get; set; } = "";
    [ObservableProperty] public partial bool WatchProblem { get; set; }

    public bool WatchOn
    {
        get => services.UptimeSettings.On;
        set
        {
            services.UptimeSettings = services.UptimeSettings with { On = value };
            OnPropertyChanged();
        }
    }

    /// <summary>Rebuilds the list from the settings, after a round of visits or a change.</summary>
    public void RefreshWatched()
    {
        var settings = services.UptimeSettings;
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(DateTime.Today);
        Watched.Clear();
        foreach (var domain in Uptime.Domains(services.Store.ListClients(), settings))
            Watched.Add(new WatchedSiteViewModel(domain, settings.Sites.GetValueOrDefault(domain.Domain), now, today, this));
        var down = Watched.Count(w => w.Down);
        var certificates = Watched.Count(w => w.CertificateProblem);
        WatchProblem = down + certificates > 0;
        WatchSummary = Watched.Count == 0 ? "Add a website to a client page, or watch one from the box above."
            : down > 0 ? (down == 1 ? "1 site is down." : $"{down} sites are down.")
            : certificates > 0 ? (certificates == 1 ? "1 certificate runs out soon." : $"{certificates} certificates run out soon.")
            : UptimeWatch.LastRun is { } last ? $"All up. Last checked at {last:HH:mm}." : "Checked every 15 minutes while Webignite is open.";
        OnPropertyChanged(nameof(HasWatched));
        OnPropertyChanged(nameof(WatchOn));
    }

    private bool CanWatch() => DomainReport.Normalise(Address) is not null;

    [RelayCommand(CanExecute = nameof(CanWatch))]
    private async Task Watch()
    {
        if (DomainReport.Normalise(Address) is not { } domain)
            return;
        services.UptimeSettings = Uptime.Add(services.UptimeSettings, domain, services.Store.ListClients());
        RefreshWatched();
        await CheckAll();
    }

    [RelayCommand]
    private async Task CheckAll()
    {
        if (UptimeWatch.Running)
            return;
        WatchSummary = "Checking...";
        WatchProblem = false;
        var alerts = await UptimeWatch.RunAsync(services);
        Announce?.Invoke(alerts);
        RefreshWatched();
    }

    internal void StopWatching(string domain)
    {
        services.UptimeSettings = Uptime.Remove(services.UptimeSettings, domain, services.Store.ListClients());
        RefreshWatched();
    }

    /// <summary>Set by the main view model: shows the notes for sites that went down or came back.</summary>
    public Action<IReadOnlyList<UptimeAlert>>? Announce { get; set; }
}

public sealed partial class WatchedSiteViewModel(WatchedDomain domain, WatchedSite? site, DateTimeOffset now, DateOnly today, SiteCheckViewModel owner) : ViewModelBase
{
    public string Domain => domain.Domain;
    public string Client => domain.Client ?? "Added here";
    public string Status => Uptime.Status(site, now);
    public bool Down => site is not null && Uptime.IsDown(site);
    public string Percent => site is null ? "" : Uptime.PercentText(site);
    public string Certificate => Uptime.Certificate(site, today);
    public bool CertificateProblem => Uptime.CertificateProblem(site, today);
    public string Note => site?.Last?.Problem ?? "";

    [RelayCommand]
    private void Stop() => owner.StopWatching(domain.Domain);
}
