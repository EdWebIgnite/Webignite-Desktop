using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;

namespace Webignite.Desktop.ViewModels;

/// <summary>Checks a client's domain (DNS, mail set-up, certificate, registration) and how fast the site loads.</summary>
public sealed partial class SiteCheckViewModel(AppServices services) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckDomainCommand), nameof(TestSpeedCommand), nameof(WatchCommand))]
    public partial string Address { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDomain))]
    public partial string DomainTitle { get; set; } = "";

    [ObservableProperty] public partial string DomainMessage { get; set; } = "";
    [ObservableProperty] public partial bool DomainProblem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpeed), nameof(SpeedScore), nameof(SpeedVerdict), nameof(SpeedIsProblem))]
    public partial SpeedReport? Speed { get; set; }

    [ObservableProperty] public partial string SpeedTitle { get; set; } = "";
    [ObservableProperty] public partial string SpeedChange { get; set; } = "";
    private SpeedRun? previousRun;
    private string speedDomain = "";
    [ObservableProperty] public partial string SpeedMessage { get; set; } = "";
    [ObservableProperty] public partial bool SpeedMessageProblem { get; set; }

    public ObservableCollection<DomainFinding> Findings { get; } = [];

    public bool HasDomain => Findings.Count > 0;
    public bool HasSpeed => Speed is not null;
    public string SpeedScore => Speed?.Score.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string SpeedVerdict => Speed is null ? "" : $"{Speed.Verdict} on a phone";
    public bool SpeedIsProblem => Speed?.Problem ?? false;
    public bool HasFixes => Speed?.Fixes.Count > 0;

    private bool CanCheck() => Domains.Of(Address) is not null && !CheckDomainCommand.IsRunning && !TestSpeedCommand.IsRunning;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckDomain()
    {
        if (Domains.Of(Address) is not { } domain)
            return;
        Findings.Clear();
        OnPropertyChanged(nameof(HasDomain));
        DomainTitle = domain;
        DomainMessage = "Looking it up…";
        DomainProblem = false;
        try
        {
            foreach (var finding in await services.SiteChecker.DomainAsync(domain))
                Findings.Add(finding);
            var problems = Findings.Count(f => f.Problem);
            DomainMessage = problems == 0 ? "Nothing needs fixing." : problems == 1 ? "1 thing needs a look." : $"{problems} things need a look.";
            DomainProblem = problems > 0;
        }
        catch (HttpRequestException e)
        {
            DomainMessage = e.Message;
            DomainProblem = true;
        }
        OnPropertyChanged(nameof(HasDomain));
    }

    // Is a domain free? A name is tried with each of the usual endings.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindFreeCommand))]
    public partial string WantedName { get; set; } = "";

    [ObservableProperty] public partial string FreeMessage { get; set; } = "";
    [ObservableProperty] public partial bool FreeWorking { get; set; }

    public ObservableCollection<DomainCheck> FreeResults { get; } = [];
    public bool HasFreeResults => FreeResults.Count > 0;

    /// <summary>The list of names was changed from outside; the page shows it.</summary>
    public void Refreshed() => OnPropertyChanged(nameof(HasFreeResults));

    private bool CanFindFree() => !FreeWorking && WantedName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanFindFree))]
    private async Task FindFree()
    {
        var names = DomainAvailability.Names(WantedName);
        FreeResults.Clear();
        OnPropertyChanged(nameof(HasFreeResults));
        if (names.Count == 0)
        {
            FreeMessage = "Type a name using letters, digits and hyphens, such as karoocoffee.";
            return;
        }
        FreeWorking = true;
        FindFreeCommand.NotifyCanExecuteChanged();
        FreeMessage = "Asking the registries…";
        try
        {
            // All at once, shown in the order of the list.
            var checks = await Task.WhenAll(names.Select(n => services.SiteChecker.AvailabilityAsync(n)));
            foreach (var check in checks)
                FreeResults.Add(check);
            var free = checks.Count(c => c.CanBeHad);
            FreeMessage = free == 0 ? "None of these is free." : free == 1 ? "1 of these is free." : $"{free} of these are free.";
        }
        finally
        {
            FreeWorking = false;
            FindFreeCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasFreeResults));
        }
    }

    /// <summary>Looks a taken name over in the main check above: who has it, where it points, its mail set-up.</summary>
    [RelayCommand]
    private async Task LookOver(DomainCheck? check)
    {
        if (check is null)
            return;
        Address = check.Domain;
        if (CheckDomainCommand.CanExecute(null))
            await CheckDomainCommand.ExecuteAsync(null);
    }

    /// <summary>The box for a Google key is showing: a test ran out of allowance, or Use my own key was clicked.</summary>
    [ObservableProperty] public partial bool AsksForSpeedKey { get; set; }

    [ObservableProperty] public partial string SpeedKeyTyped { get; set; } = "";

    public bool HasSpeedKey => services.Settings.SpeedKey.Length > 0;
    public string SpeedKeyLink => HasSpeedKey ? "Change the Google key" : "Use my own Google key";

    [RelayCommand]
    private void AskForSpeedKey()
    {
        SpeedKeyTyped = services.Settings.SpeedKey;
        AsksForSpeedKey = !AsksForSpeedKey;
    }

    /// <summary>Keeps the key (an empty box takes it away) and runs the test again with it.</summary>
    [RelayCommand]
    private async Task SaveSpeedKey()
    {
        services.Settings.SpeedKey = SpeedKeyTyped;
        AsksForSpeedKey = false;
        OnPropertyChanged(nameof(HasSpeedKey));
        OnPropertyChanged(nameof(SpeedKeyLink));
        if (SpeedMessageProblem && TestSpeedCommand.CanExecute(null))
            await TestSpeedCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private static void GetSpeedKey() =>
        AppServices.OpenInBrowser(new Uri("https://developers.google.com/speed/docs/insights/v5/get-started#APIKey")).Forget("Opening Google's page for a key", tell: true);

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task TestSpeed()
    {
        if (SpeedReport.Address(Address) is not { } address)
            return;
        Speed = null;
        SpeedChange = "";
        SpeedTitle = address;
        SpeedMessage = "Google is loading the site the way a phone would. This takes up to a minute…";
        SpeedMessageProblem = false;
        try
        {
            var report = await services.SiteChecker.SpeedAsync(address, services.Settings.SpeedKey);
            Remember(address, report);
            Speed = report;
            SpeedMessage = "";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            SpeedMessage = e is HttpRequestException ? e.Message : "Google's test didn't finish. Try again in a minute.";
            SpeedMessageProblem = true;
            // Out of allowance, or the key was refused: the place to put a key in is shown with the reason.
            if (e is Webignite.Services.SpeedQuotaException or Webignite.Services.SpeedKeyException)
                AsksForSpeedKey = true;
        }
        OnPropertyChanged(nameof(HasFixes));
    }

    // Keeps the score, and says how it compares with the last test of the same site.
    private void Remember(string address, SpeedReport report)
    {
        speedDomain = Domains.Of(address) ?? address;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var history = services.Settings.SpeedHistory;
        previousRun = SpeedHistory.Previous(history, speedDomain, today);
        SpeedChange = SpeedHistory.Compare(previousRun, report.Score);
        services.Settings.SpeedHistory = SpeedHistory.Add(history, speedDomain, report, today);
    }

    [RelayCommand]
    private async Task CopyReport(Avalonia.Controls.TopLevel? top)
    {
        if (Speed is not { } report || top?.Clipboard is not { } clipboard)
            return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard,
            SpeedHistory.ReportText(speedDomain, report, previousRun, DateOnly.FromDateTime(DateTime.Today)));
        SpeedMessage = "Copied. Paste it into a mail or report.";
        SpeedMessageProblem = false;
    }

    /// <summary>For the screenshot tool: shows results without the network.</summary>
    public void Show(string domain, IEnumerable<DomainFinding> findings, string address, SpeedReport speed)
    {
        Address = domain;
        DomainTitle = domain;
        Findings.Clear();
        foreach (var finding in findings)
            Findings.Add(finding);
        var problems = Findings.Count(f => f.Problem);
        DomainMessage = problems == 0 ? "Nothing needs fixing." : problems == 1 ? "1 thing needs a look." : $"{problems} things need a look.";
        DomainProblem = problems > 0;
        SpeedTitle = address;
        Remember(address, speed);
        Speed = speed;
        OnPropertyChanged(nameof(HasDomain));
        OnPropertyChanged(nameof(HasFixes));
    }
}
