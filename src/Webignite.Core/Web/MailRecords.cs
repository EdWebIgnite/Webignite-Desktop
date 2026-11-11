using System.Net;

namespace Webignite.Core.Web;

/// <summary>A mail service a domain may send through, and what its SPF record must include for it.</summary>
public sealed record MailSender(string Name, string Include);

/// <summary>How strictly mail that fails SPF is to be treated.</summary>
public enum SpfEnding
{
    /// <summary>~all: mark it as suspect. The safe choice while setting up.</summary>
    Soft,

    /// <summary>-all: refuse it. Only once every sender is in the record.</summary>
    Hard,
}

public sealed record SpfChoices
{
    /// <summary>The servers that receive this domain's mail may send it too (mx).</summary>
    public bool MailServers { get; init; } = true;

    /// <summary>The website's own server may send mail, as contact forms do (a).</summary>
    public bool WebServer { get; init; }

    public IReadOnlyList<MailSender> Senders { get; init; } = [];

    /// <summary>Anything else, as typed: other services' includes (a domain) and servers' addresses.</summary>
    public string Others { get; init; } = "";

    public SpfEnding Ending { get; init; } = SpfEnding.Soft;
}

/// <summary>A record ready to paste in at the host: where it goes, its kind, what it says, and anything to know.</summary>
public sealed record BuiltRecord(string Host, string Type, string Value, IReadOnlyList<string> Notes);

public enum DmarcPolicy
{
    None,
    Quarantine,
    Reject,
}

/// <summary>
/// Builds the DNS records that decide whether a domain's mail is trusted: SPF (who may send) and DMARC (what to
/// do with mail that fails). DKIM keys are made by the mail host, so there's nothing to build, only where it goes.
/// </summary>
public static class MailRecords
{
    /// <summary>The services worth a tick-box. Each include is the one the service itself publishes.</summary>
    public static readonly IReadOnlyList<MailSender> KnownSenders =
    [
        new("xneelo", "spf.host-h.net"),
        new("Google Workspace", "_spf.google.com"),
        new("Microsoft 365", "spf.protection.outlook.com"),
        new("Mailchimp", "servers.mcsv.net"),
        new("Brevo", "spf.brevo.com"),
        new("SendGrid", "sendgrid.net"),
        new("Mailgun", "mailgun.org"),
        new("Zoho Mail", "zoho.com"),
    ];

    /// <summary>A mail server may look up at most this many names while reading one SPF record.</summary>
    public const int MostLookups = 10;

    public static BuiltRecord Spf(SpfChoices choices)
    {
        var parts = new List<string> { "v=spf1" };
        var notes = new List<string>();
        if (choices.MailServers)
            parts.Add("mx");
        if (choices.WebServer)
            parts.Add("a");
        foreach (var sender in choices.Senders)
            parts.Add("include:" + sender.Include);
        foreach (var typed in choices.Others.Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var other = typed.Trim();
            var bare = other.Contains(':') && !IPAddress.TryParse(other, out _) && !other.Contains('/') ? other[(other.IndexOf(':') + 1)..] : other;
            var slash = other.Split('/')[0];
            if (other.StartsWith("include:", StringComparison.OrdinalIgnoreCase) || other.StartsWith("ip4:", StringComparison.OrdinalIgnoreCase)
                || other.StartsWith("ip6:", StringComparison.OrdinalIgnoreCase) || other.StartsWith("a:", StringComparison.OrdinalIgnoreCase)
                || other.StartsWith("mx:", StringComparison.OrdinalIgnoreCase))
                Add(other);
            else if (IPAddress.TryParse(slash, out var address))
                Add((address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "ip6:" : "ip4:") + other);
            else if (Domains.Of(bare) is { } domain)
                Add("include:" + domain);
            else
                notes.Add($"\"{other}\" isn't a server's address or a service's domain, so it was left out.");
        }
        parts.Add(choices.Ending == SpfEnding.Hard ? "-all" : "~all");

        var value = string.Join(' ', parts);
        if (parts.Count == 2)
            notes.Add("Nothing may send mail for this domain with this record. That is right only for a domain that sends no mail at all.");
        var lookups = Lookups(value);
        if (lookups > MostLookups)
            notes.Add($"This needs {lookups} lookups and mail servers stop at {MostLookups}, so it would fail. Take out a service that isn't used.");
        if (value.Length > 255)
            notes.Add("It is longer than 255 characters. Most hosts split it for you; if yours complains, take out a sender.");
        if (choices.Ending == SpfEnding.Hard)
            notes.Add("-all refuses mail from anything not listed. Make sure every service that sends for this domain is ticked.");
        notes.Add("A domain may have only one SPF record. If there is one already, change it rather than adding this beside it.");
        return new BuiltRecord("@", "TXT", value, notes);

        void Add(string part)
        {
            if (!parts.Contains(part, StringComparer.OrdinalIgnoreCase))
                parts.Add(part);
        }
    }

    /// <summary>How many names a mail server must look up to read this SPF record (the limit is 10).</summary>
    public static int Lookups(string spf) =>
        spf.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(p =>
            p is "a" or "mx" or "ptr" || p.StartsWith("include:", StringComparison.OrdinalIgnoreCase) || p.StartsWith("a:", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("mx:", StringComparison.OrdinalIgnoreCase) || p.StartsWith("exists:", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("redirect=", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A DMARC record. <paramref name="reportsTo"/> is where the daily reports go; without one the record still
    /// works but nobody sees who is sending as the domain.
    /// </summary>
    public static BuiltRecord Dmarc(DmarcPolicy policy, string? reportsTo, int percent = 100)
    {
        var notes = new List<string>();
        var parts = new List<string> { "v=DMARC1", "p=" + policy.ToString().ToLowerInvariant() };
        var address = (reportsTo ?? "").Trim();
        if (address.Length > 0)
        {
            if (address.Count(c => c == '@') == 1 && Domains.Of(address) is not null && !address.Contains(' '))
                parts.Add("rua=mailto:" + address);
            else
                notes.Add("That doesn't look like an email address, so no reports are asked for.");
        }
        else
            notes.Add("With no address for reports, nobody sees who is sending mail as this domain. Add one to find out before tightening the policy.");
        percent = Math.Clamp(percent, 1, 100);
        if (percent < 100 && policy != DmarcPolicy.None)
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"pct={percent}"));
        notes.Add(policy switch
        {
            DmarcPolicy.None => "p=none only watches: nothing is blocked. Start here, read the reports for a few weeks, then move to quarantine.",
            DmarcPolicy.Quarantine => "p=quarantine sends failing mail to spam. Use it once the reports show only your own senders.",
            _ => "p=reject refuses failing mail outright. Only once SPF and DKIM pass for everything that sends as this domain.",
        });
        return new BuiltRecord("_dmarc", "TXT", string.Join("; ", parts), notes);
    }

    /// <summary>What a domain has now, as the site check words it, without the DKIM guesswork.</summary>
    public static IReadOnlyList<DomainFinding> Now(IReadOnlyList<string> txt, IReadOnlyList<string> dmarcTxt, bool hasMail) =>
        DomainReport.MailAuth(txt, dmarcTxt, hasMail);

    /// <summary>Where a DKIM key goes, for the selector the mail host gives: "default" becomes "default._domainkey".</summary>
    public static string DkimHost(string? selector)
    {
        var name = (selector ?? "").Trim().Trim('.').ToLowerInvariant();
        const string ending = "._domainkey";
        if (name.EndsWith(ending, StringComparison.Ordinal))
            name = name[..^ending.Length];
        return name.Length == 0 ? "" : name + ending;
    }

    /// <summary>What a DKIM record found in DNS says about itself.</summary>
    public static string DkimVerdict(IReadOnlyList<string> txt)
    {
        var record = txt.FirstOrDefault(t => t.Contains("p=", StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return txt.Count == 0 ? "Nothing is there. Check the selector with the mail host: it is the name before ._domainkey." : "There is a record, but it isn't a DKIM key.";
        var key = record.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("p=", StringComparison.OrdinalIgnoreCase))?[2..].Trim() ?? "";
        return key.Length == 0
            ? "The key has been withdrawn (p= is empty), so mail signed with this selector fails."
            : key.Length < 200 ? "A DKIM key is there. It is a short one (1024 bits); ask the host for a 2048-bit key when it is next changed." : "A DKIM key is there and looks right.";
    }
}
