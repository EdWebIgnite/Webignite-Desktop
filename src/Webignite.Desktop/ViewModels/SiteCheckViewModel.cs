using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;

namespace Webignite.Desktop.ViewModels;

/// <summary>Checks a client's domain (DNS, mail set-up, certificate, registration) and how fast the site loads.</summary>
public sealed partial class SiteCheckViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckDomainCommand), nameof(TestSpeedCommand))]
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
    [ObservableProperty] public partial string SpeedMessage { get; set; } = "";
    [ObservableProperty] public partial bool SpeedMessageProblem { get; set; }

    public ObservableCollection<DomainFinding> Findings { get; } = [];

    public bool HasDomain => Findings.Count > 0;
    public bool HasSpeed => Speed is not null;
    public string SpeedScore => Speed?.Score.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string SpeedVerdict => Speed is null ? "" : $"{Speed.Verdict} on a phone";
    public bool SpeedIsProblem => Speed?.Problem ?? false;
    public bool HasFixes => Speed?.Fixes.Count > 0;

    private bool CanCheck() => DomainReport.Normalise(Address) is not null && !CheckDomainCommand.IsRunning && !TestSpeedCommand.IsRunning;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckDomain()
    {
        if (DomainReport.Normalise(Address) is not { } domain)
            return;
        Findings.Clear();
        OnPropertyChanged(nameof(HasDomain));
        DomainTitle = domain;
        DomainMessage = "Looking it up...";
        DomainProblem = false;
        try
        {
            foreach (var finding in await SiteChecker.DomainAsync(domain))
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

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task TestSpeed()
    {
        if (SpeedReport.Address(Address) is not { } address)
            return;
        Speed = null;
        SpeedTitle = address;
        SpeedMessage = "Google is loading the site the way a phone would. This takes up to a minute...";
        SpeedMessageProblem = false;
        try
        {
            Speed = await SiteChecker.SpeedAsync(address);
            SpeedMessage = "";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            SpeedMessage = e is HttpRequestException ? e.Message : "Google's test didn't finish. Try again in a minute.";
            SpeedMessageProblem = true;
        }
        OnPropertyChanged(nameof(HasFixes));
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
        Speed = speed;
        OnPropertyChanged(nameof(HasDomain));
        OnPropertyChanged(nameof(HasFixes));
    }
}
