using System.Globalization;
using System.Text.Json;

namespace Webignite.Core.Web;

/// <summary>One line of a domain check. A problem shows in red.</summary>
public sealed record DomainFinding(string Label, string Value, bool Problem = false);

/// <summary>What was looked up for a domain, raw, so the rules can be tested without a network.</summary>
public sealed record DomainLookup
{
    public required string Domain { get; init; }
    public required DateOnly Today { get; init; }
    public IReadOnlyList<string> Addresses { get; init; } = [];
    public IReadOnlyList<string> NameServers { get; init; } = [];
    public IReadOnlyList<string> MailServers { get; init; } = [];
    public IReadOnlyList<string> Txt { get; init; } = [];
    public IReadOnlyList<string> DmarcTxt { get; init; } = [];
    public string? RdapJson { get; init; }
    public string? RdapProblem { get; init; }
    public DateTimeOffset? CertificateExpires { get; init; }
    public string? CertificateIssuer { get; init; }
    public string? CertificateProblem { get; init; }
    /// <summary>TXT records found at each DKIM selector tried (selector._domainkey.domain). Null when not looked up.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Dkim { get; init; }
}

/// <summary>
/// Turns a domain's DNS, registration and certificate into plain findings: where it points, who hosts the
/// mail, when it expires, and whether SPF and DMARC are set up so invoices don't land in spam.
/// </summary>
public static class DomainReport
{
    public const int CertificateWarnDays = 14;
    public const int RegistrationWarnDays = 30;

    /// <summary>"https://www.Karoo.co.za/menu" becomes "karoo.co.za". Null when it isn't a domain.</summary>
    public static string? Normalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var text = input.Trim().ToLowerInvariant();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            text = text[(scheme + 3)..];
        text = text.Split('/', '?', '#')[0].Split(':')[0].TrimEnd('.');
        if (text.Contains('@'))
            text = text[(text.LastIndexOf('@') + 1)..];
        if (text.StartsWith("www.", StringComparison.Ordinal))
            text = text[4..];
        if (text.Length is 0 or > 253 || !text.Contains('.'))
            return null;
        foreach (var label in text.Split('.'))
        {
            if (label.Length is 0 or > 63 || label.StartsWith('-') || label.EndsWith('-') || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                return null;
        }
        return text;
    }

    /// <summary>The answers in a DNS-over-HTTPS JSON reply, of one record type (1 A, 2 NS, 15 MX, 16 TXT).</summary>
    public static IReadOnlyList<string> DnsAnswers(string json, int type)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var answer in answers.EnumerateArray())
        {
            if (answer.TryGetProperty("type", out var t) && t.GetInt32() == type && answer.TryGetProperty("data", out var data))
                list.Add(Clean(data.GetString() ?? "", type));
        }
        return list;
    }

    // TXT records arrive quoted, sometimes split into several quoted pieces; MX as "10 mail.example.com.".
    private static string Clean(string data, int type)
    {
        if (type == 16)
            return string.Concat(data.Split('"').Where((_, i) => i % 2 == 1));
        return data.TrimEnd('.');
    }

    public static IReadOnlyList<DomainFinding> Build(DomainLookup look)
    {
        var findings = new List<DomainFinding>();

        if (look.Addresses.Count == 0)
            findings.Add(new("Website", "The domain doesn't point anywhere. No website will load.", true));
        else
            findings.Add(new("Website", "Points to " + string.Join(", ", look.Addresses)));

        if (look.CertificateProblem is { } certProblem)
            findings.Add(new("Security certificate", certProblem, true));
        else if (look.CertificateExpires is { } expires)
        {
            var days = DateOnly.FromDateTime(expires.UtcDateTime).DayNumber - look.Today.DayNumber;
            var who = look.CertificateIssuer is { Length: > 0 } issuer ? $" from {issuer}" : "";
            findings.Add(days < 0
                ? new("Security certificate", $"Expired {Ago(-days)}. Visitors get a warning page.", true)
                : new("Security certificate", $"Valid{who} until {Date(expires)} ({Days(days)} left)." +
                    (days <= CertificateWarnDays ? " It should renew by itself soon; check if it doesn't." : ""), days <= CertificateWarnDays));
        }

        findings.AddRange(Registration(look));

        findings.Add(look.NameServers.Count == 0
            ? new("DNS is at", "No name servers found.", true)
            : new("DNS is at", string.Join(", ", look.NameServers.Order(StringComparer.Ordinal)) + Host(look.NameServers)));

        if (look.MailServers.Count == 0)
            findings.Add(new("Mail", "No mail servers, so mail to this domain bounces.", true));
        else
        {
            var servers = look.MailServers.Select(m => m.Split(' ', 2) is [var pref, var host] && int.TryParse(pref, out var p) ? (p, host) : (0, m))
                .OrderBy(m => m.Item1).Select(m => m.Item2).ToList();
            findings.Add(new("Mail", "Goes to " + string.Join(", ", servers) + Host(servers)));
        }

        findings.AddRange(MailAuth(look.Txt, look.DmarcTxt, look.MailServers.Count > 0, look.Dkim));
        return findings;
    }

    /// <summary>SPF and DMARC: what decides whether this domain's mail is trusted or sent to spam.</summary>
    public static IReadOnlyList<DomainFinding> MailAuth(IReadOnlyList<string> txt, IReadOnlyList<string> dmarcTxt, bool hasMail,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? dkim = null)
    {
        var findings = new List<DomainFinding>();
        var spf = txt.Where(t => t.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)).ToList();
        if (spf.Count == 0)
            findings.Add(new("SPF", hasMail ? "Missing. Mail from this domain is more likely to land in spam." : "None, which is fine for a domain that sends no mail.", hasMail));
        else if (spf.Count > 1)
            findings.Add(new("SPF", "There are two SPF records. Mail servers then ignore both; merge them into one.", true));
        else
        {
            var record = spf[0];
            var ending = record.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            var lookups = record.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Count(p => p.StartsWith("include:", StringComparison.OrdinalIgnoreCase) || p is "a" or "mx" || p.StartsWith("a:", StringComparison.OrdinalIgnoreCase)
                    || p.StartsWith("mx:", StringComparison.OrdinalIgnoreCase) || p.StartsWith("redirect=", StringComparison.OrdinalIgnoreCase) || p.StartsWith("exists:", StringComparison.OrdinalIgnoreCase));
            if (ending is "+all" or "all")
                findings.Add(new("SPF", record + ". It ends in +all, which lets anyone send as this domain. Use ~all.", true));
            else if (lookups > 10)
                findings.Add(new("SPF", record + $". It has {lookups} lookups; more than 10 makes it fail.", true));
            else
                findings.Add(new("SPF", record));
        }

        if (dkim is not null)
            findings.Add(Dkim(dkim, hasMail));

        var dmarc = dmarcTxt.FirstOrDefault(t => t.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
        if (dmarc is null)
            findings.Add(new("DMARC", hasMail ? "Missing. Gmail and Outlook trust mail more with one. Start with v=DMARC1; p=none" : "None.", hasMail));
        else
        {
            var policy = dmarc.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("p=", StringComparison.OrdinalIgnoreCase))?[2..].ToLowerInvariant();
            findings.Add(new("DMARC", dmarc + policy switch
            {
                "none" => ". Watching only; fine to start, move to quarantine once reports look clean.",
                "quarantine" or "reject" => "",
                _ => ". It has no p= policy, so it does nothing.",
            }, policy is not ("none" or "quarantine" or "reject")));
        }
        return findings;
    }

    /// <summary>
    /// The DKIM selectors worth trying. DKIM keys sit under a name only the mail host knows, so the usual
    /// names for Google, Microsoft 365, cPanel hosts like xneelo, and the bulk mail services are tried.
    /// </summary>
    public static readonly IReadOnlyList<string> DkimSelectors =
        ["default", "google", "selector1", "selector2", "k1", "k2", "s1", "s2", "dkim", "mail", "smtp", "mx", "zoho"];

    /// <summary>What the DKIM lookups found. Not finding one isn't proof there is none, so that isn't shown as a problem.</summary>
    public static DomainFinding Dkim(IReadOnlyDictionary<string, IReadOnlyList<string>> found, bool hasMail)
    {
        var keys = found.Where(f => f.Value.Any(IsDkimKey)).ToList();
        var live = keys.Where(f => f.Value.Any(v => IsDkimKey(v) && KeyValue(v) is not { Length: 0 })).Select(f => f.Key).Order(StringComparer.Ordinal).ToList();
        if (live.Count > 0)
            return new("DKIM", $"Set up. Found under {string.Join(", ", live)}.");
        if (keys.Count > 0)
            return new("DKIM", $"Found under {string.Join(", ", keys.Select(k => k.Key).Order(StringComparer.Ordinal))}, but the key is empty, so mail isn't signed. Switch DKIM on again at the mail host.", true);
        return new("DKIM", hasMail
            ? "None found under the usual names. It may use a name only the mail host knows; if not, ask them to switch DKIM on."
            : "None.");
    }

    private static bool IsDkimKey(string record) =>
        record.StartsWith("v=DKIM1", StringComparison.OrdinalIgnoreCase) || record.Split(';').Any(p => p.Trim().StartsWith("p=", StringComparison.OrdinalIgnoreCase));

    private static string? KeyValue(string record) =>
        record.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("p=", StringComparison.OrdinalIgnoreCase))?[2..].Trim();

    private static IEnumerable<DomainFinding> Registration(DomainLookup look)
    {
        if (look.RdapJson is null)
        {
            yield return new("Registration", look.RdapProblem ?? "The registry didn't say when it expires. Check with the registrar.");
            yield break;
        }
        using var doc = JsonDocument.Parse(look.RdapJson);
        var root = doc.RootElement;
        DateTimeOffset? expires = null;
        if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in events.EnumerateArray())
            {
                if (e.TryGetProperty("eventAction", out var action) && action.GetString() == "expiration"
                    && e.TryGetProperty("eventDate", out var date) && DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
                    expires = when;
            }
        }
        var registrar = Registrar(root);
        var by = registrar is null ? "" : $" through {registrar}";
        if (expires is not { } until)
        {
            yield return new("Registration", $"Registered{by}. The registry didn't say when it expires.");
            yield break;
        }
        var days = DateOnly.FromDateTime(until.UtcDateTime).DayNumber - look.Today.DayNumber;
        yield return days < 0
            ? new("Registration", $"Expired {Ago(-days)}{by}. Renew it now or it can be lost.", true)
            : new("Registration", $"Registered{by} until {Date(until)} ({Days(days)} left).", days <= RegistrationWarnDays);
    }

    // The registrar is the entity with the "registrar" role; its name sits in a vCard.
    private static string? Registrar(JsonElement root)
    {
        if (!root.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var entity in entities.EnumerateArray())
        {
            if (!entity.TryGetProperty("roles", out var roles) || !roles.EnumerateArray().Any(r => r.GetString() == "registrar"))
                continue;
            if (entity.TryGetProperty("vcardArray", out var vcard) && vcard.ValueKind == JsonValueKind.Array && vcard.GetArrayLength() > 1)
            {
                foreach (var field in vcard[1].EnumerateArray())
                {
                    if (field.ValueKind == JsonValueKind.Array && field.GetArrayLength() > 3 && field[0].GetString() == "fn")
                        return field[3].GetString();
                }
            }
            if (entity.TryGetProperty("handle", out var handle))
                return handle.GetString();
        }
        return null;
    }

    // Names the hosting company when the servers say who it is.
    private static string Host(IEnumerable<string> servers)
    {
        var all = string.Join(" ", servers).ToLowerInvariant();
        var known = new (string Part, string Name)[]
        {
            ("xneelo", "xneelo"), ("host-h.net", "xneelo"), ("dns-h.com", "xneelo"), ("hetzner", "Hetzner"), ("afrihost", "Afrihost"), ("cloudflare", "Cloudflare"),
            ("google.com", "Google"), ("googlemail", "Google"), ("outlook.com", "Microsoft 365"), ("azure-dns", "Microsoft Azure"),
            ("domains.co.za", "Domains.co.za"), ("register-dom", "Register Domain SA"), ("awsdns", "Amazon"), ("wix", "Wix"),
        };
        var match = known.FirstOrDefault(k => all.Contains(k.Part, StringComparison.Ordinal));
        return match.Name is null ? "" : $" ({match.Name})";
    }

    private static string Date(DateTimeOffset when) => when.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";
    private static string Ago(int days) => days == 0 ? "today" : days == 1 ? "yesterday" : $"{days} days ago";
}
