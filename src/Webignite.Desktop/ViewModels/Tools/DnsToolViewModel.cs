using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;

namespace Webignite.Desktop.ViewModels;

/// <summary>The parts of the DNS records window, one shown at a time.</summary>
public enum DnsPart
{
    Lookup,
    Spread,
    Mail,
}

// DNS records: look up any record, see whether a change has spread, and build the SPF and DMARC records that
// decide whether a domain's mail is trusted. Read-only: nothing is changed at any host. The rules are in Core
// (DnsMessage, DnsSpread, MailRecords) and the asking in Services (DnsClient).
public sealed partial class DnsToolViewModel : ViewModelBase
{
    private readonly AppServices services;

    public DnsToolViewModel(AppServices services)
    {
        this.services = services;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDnsLookup), nameof(IsDnsSpread), nameof(IsDnsMail))]
    public partial DnsPart DnsPart { get; set; }

    public bool IsDnsLookup => DnsPart == DnsPart.Lookup;
    public bool IsDnsSpread => DnsPart == DnsPart.Spread;
    public bool IsDnsMail => DnsPart == DnsPart.Mail;

    [RelayCommand]
    private void ShowDnsPart(DnsPart part) => DnsPart = part;

    // Look up, and Has it spread: the same name and kind for both.

    public IReadOnlyList<DnsType> DnsTypes { get; } =
        [DnsType.A, DnsType.AAAA, DnsType.CNAME, DnsType.MX, DnsType.TXT, DnsType.NS, DnsType.SOA, DnsType.CAA, DnsType.SRV, DnsType.PTR];

    [ObservableProperty] public partial string DnsName { get; set; } = "";
    [ObservableProperty] public partial DnsType DnsTypeChoice { get; set; } = DnsType.A;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpDnsCommand), nameof(CheckSpreadCommand), nameof(ReadMailRecordsCommand), nameof(CheckDkimCommand))]
    public partial bool DnsBusy { get; set; }

    [ObservableProperty] public partial string DnsNote { get; set; } = "";

    /// <summary>"The record copied.", under whichever part it was copied from.</summary>
    [ObservableProperty] public partial string CopiedMessage { get; set; } = "";

    /// <summary>Copies a record or an answer; the button passes the text and the view its window.</summary>
    public async Task CopyAsync(Avalonia.Controls.TopLevel? top, string? text, string what)
    {
        if (await ToolClipboard.CopyAsync(top, text))
            CopiedMessage = what + " copied.";
    }
    [ObservableProperty] public partial bool DnsProblem { get; set; }
    [ObservableProperty] public partial string SpreadVerdict { get; set; } = "";

    public ObservableCollection<DnsRow> DnsRows { get; } = [];
    public ObservableCollection<SpreadRow> SpreadRows { get; } = [];

    public bool HasDnsRows => DnsRows.Count > 0;
    public bool HasSpreadRows => SpreadRows.Count > 0;
    private bool DnsIdle => !DnsBusy;

    [RelayCommand(CanExecute = nameof(DnsIdle))]
    private Task LookUpDns() => AskDns(async name =>
    {
        var answer = await services.Dns.LookupAsync(name, DnsTypeChoice);
        ShowDnsAnswer(name, answer);
    });

    [RelayCommand(CanExecute = nameof(DnsIdle))]
    private Task CheckSpread() => AskDns(async name =>
    {
        var result = await services.Dns.SpreadAsync(name, DnsTypeChoice);
        ShowSpread(result.Sightings, result.TtlSeconds);
    });

    /// <summary>Shows a lookup's records, or says why there are none.</summary>
    public void ShowDnsAnswer(string name, DnsAnswer answer)
    {
        DnsRows.Clear();
        // An alias's own record comes first, then what it leads to.
        foreach (var record in answer.Records.OrderBy(r => r.Type == DnsTypeChoice ? 1 : 0).ThenBy(r => r.Value, StringComparer.OrdinalIgnoreCase))
            DnsRows.Add(new DnsRow(record.Type.ToString(), record.Value, "kept " + DnsMessage.Lifetime(record.Ttl)));
        OnPropertyChanged(nameof(HasDnsRows));
        DnsProblem = answer.NoSuchName || answer.Refused;
        DnsNote = answer.NoSuchName ? $"{name} doesn't exist. Check the spelling, or it may not be set up yet."
            : answer.Refused ? "The resolver wouldn't answer for that name."
            : DnsRows.Count == 0 ? $"{name} exists, but has no {DnsTypeChoice} record."
            : "";
    }

    /// <summary>Shows what each place said, and the verdict.</summary>
    public void ShowSpread(IReadOnlyList<DnsSighting> sightings, int? ttlSeconds)
    {
        SpreadRows.Clear();
        foreach (var sighting in sightings)
        {
            var matches = DnsSpread.Matches(sighting, sightings);
            SpreadRows.Add(new SpreadRow(
                sighting.Where,
                sighting.IsOwnServer ? "The domain's own name server" : "Public resolver",
                sighting.Problem ?? (sighting.Values.Count == 0 ? "No record" : string.Join(", ", sighting.Values)),
                sighting.Problem is not null ? "" : sighting.IsOwnServer ? "" : matches switch { true => "Up to date", false => "Older answer", _ => "" },
                matches == false));
        }
        OnPropertyChanged(nameof(HasSpreadRows));
        SpreadVerdict = DnsSpread.Verdict(sightings, ttlSeconds);
        DnsNote = "";
        DnsProblem = false;
    }

    private async Task AskDns(Func<string, Task> ask)
    {
        if (DnsMessage.NameFor(DnsName, DnsTypeChoice) is not { } name)
        {
            DnsNote = DnsTypeChoice == DnsType.PTR ? "Type a server's address, such as 41.203.18.10." : "Type a domain, such as karoocoffee.co.za.";
            DnsProblem = true;
            return;
        }
        DnsBusy = true;
        DnsNote = "Asking…";
        DnsProblem = false;
        try
        {
            await ask(name);
        }
        catch (HttpRequestException e)
        {
            DnsNote = e.Message;
            DnsProblem = true;
        }
        finally
        {
            DnsBusy = false;
        }
    }

    // Mail records

    [ObservableProperty] public partial string MailDomain { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpfRecord))]
    public partial bool SpfMailServers { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpfRecord))]
    public partial bool SpfWebServer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpfRecord))]
    public partial string SpfOthers { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpfRecord))]
    public partial bool SpfHard { get; set; }

    private IReadOnlyList<SenderChoice>? senders;

    public IReadOnlyList<SenderChoice> SpfSenders => senders ??=
        [.. MailRecords.KnownSenders.Select(s => new SenderChoice(s, () => OnPropertyChanged(nameof(SpfRecord))))];

    public BuiltRecord SpfRecord => MailRecords.Spf(new SpfChoices
    {
        MailServers = SpfMailServers,
        WebServer = SpfWebServer,
        Senders = [.. SpfSenders.Where(s => s.IsOn).Select(s => s.Sender)],
        Others = SpfOthers,
        Ending = SpfHard ? SpfEnding.Hard : SpfEnding.Soft,
    });

    public IReadOnlyList<string> DmarcPolicies { get; } = ["Only watch (none)", "Send failures to spam (quarantine)", "Refuse failures (reject)"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DmarcRecord))]
    public partial string DmarcPolicyChoice { get; set; } = "Only watch (none)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DmarcRecord))]
    public partial string DmarcReports { get; set; } = "";

    public BuiltRecord DmarcRecord => MailRecords.Dmarc((DmarcPolicy)Math.Max(0, DmarcPolicies.ToList().IndexOf(DmarcPolicyChoice)), DmarcReports);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DkimHost), nameof(HasDkimHost))]
    public partial string DkimSelector { get; set; } = "";

    [ObservableProperty] public partial string DkimVerdict { get; set; } = "";

    public string DkimHost => MailRecords.DkimHost(DkimSelector);
    public bool HasDkimHost => DkimHost.Length > 0;

    /// <summary>What the domain has now, in the site check's own words.</summary>
    public ObservableCollection<DomainFinding> MailNow { get; } = [];

    public bool HasMailNow => MailNow.Count > 0;

    [ObservableProperty] public partial string MailNote { get; set; } = "";

    [RelayCommand(CanExecute = nameof(DnsIdle))]
    private Task ReadMailRecords() => AskMail(async domain =>
    {
        var txt = services.Dns.LookupAsync(domain, DnsType.TXT);
        var dmarc = services.Dns.LookupAsync("_dmarc." + domain, DnsType.TXT);
        var mx = services.Dns.LookupAsync(domain, DnsType.MX);
        await Task.WhenAll(txt, dmarc, mx);
        ShowMailNow(MailRecords.Now(txt.Result.Values(DnsType.TXT), dmarc.Result.Values(DnsType.TXT), mx.Result.Values(DnsType.MX).Count > 0));
    });

    [RelayCommand(CanExecute = nameof(DnsIdle))]
    private Task CheckDkim() => AskMail(async domain =>
    {
        if (!HasDkimHost)
        {
            DkimVerdict = "Type the selector the mail host gave, such as default or google.";
            return;
        }
        DkimVerdict = MailRecords.DkimVerdict((await services.Dns.LookupAsync($"{DkimHost}.{domain}", DnsType.TXT)).Values(DnsType.TXT));
    });

    public void ShowMailNow(IReadOnlyList<DomainFinding> findings)
    {
        MailNow.Clear();
        foreach (var finding in findings)
            MailNow.Add(finding);
        OnPropertyChanged(nameof(HasMailNow));
    }

    private async Task AskMail(Func<string, Task> ask)
    {
        if (Domains.Of(MailDomain) is not { } domain)
        {
            MailNote = "Type the domain first, such as karoocoffee.co.za.";
            return;
        }
        DnsBusy = true;
        MailNote = "Asking…";
        try
        {
            await ask(domain);
            MailNote = "";
        }
        catch (HttpRequestException e)
        {
            MailNote = e.Message;
        }
        finally
        {
            DnsBusy = false;
        }
    }
}

public sealed record DnsRow(string Type, string Value, string Kept);

public sealed record SpreadRow(string Where, string Kind, string Says, string State, bool Behind);

/// <summary>A mail service's tick-box in the SPF builder.</summary>
public sealed partial class SenderChoice(MailSender sender, Action changed) : ObservableObject
{
    public MailSender Sender { get; } = sender;
    public string Name => Sender.Name;

    [ObservableProperty] public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value) => changed();
}
