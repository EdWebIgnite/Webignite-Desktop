using Webignite.Core.Clients;

namespace Webignite.Core.Mail;

/// <summary>
/// Which mail belongs to a client: mail from or to their addresses, or anyone at their own domain.
/// Shared mail providers like gmail.com never count as a client's domain.
/// </summary>
public sealed class ClientMail
{
    private static readonly HashSet<string> SharedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com", "yahoo.co.za",
        "icloud.com", "me.com", "mac.com", "aol.com", "proton.me", "protonmail.com", "zoho.com", "gmx.com", "gmx.net",
        "mweb.co.za", "telkomsa.net", "vodamail.co.za", "webmail.co.za", "iafrica.com", "absamail.co.za", "lantic.net",
        "xsinet.co.za", "cybersmart.co.za", "afrihost.co.za", "axxess.co.za", "vox.co.za", "mtnloaded.co.za",
        "webignite.co.za",
    };

    private readonly HashSet<string> addresses;
    private readonly HashSet<string> domains;

    public ClientMail(Client client)
    {
        addresses = new HashSet<string>(Recipients.Parse(client.Email).People.Select(p => p.Address), StringComparer.OrdinalIgnoreCase);
        domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in addresses)
            AddDomain(address[(address.LastIndexOf('@') + 1)..]);
        if (Webignite.Core.Web.Domains.Of(client.Website) is { } site)
            AddDomain(site);
    }

    public IReadOnlyCollection<string> Addresses => addresses;

    /// <summary>Whether this is one of the client's own addresses (not just their domain).</summary>
    public bool IsOwn(string? address) => address is not null && addresses.Contains(address.Trim());

    /// <summary>
    /// The client an address belongs to: one that has the address itself, else one whose addresses or website
    /// share its domain. Shared mail providers never count. Null when none fits.
    /// </summary>
    public static Client? For(IEnumerable<Client> clients, string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@'))
            return null;
        var known = clients.Select(c => (Client: c, Mail: new ClientMail(c))).ToList();
        var person = new MailPerson(null, address.Trim());
        return known.FirstOrDefault(x => x.Mail.IsOwn(person.Address)).Client
            ?? known.FirstOrDefault(x => x.Mail.Matches(person)).Client;
    }

    /// <summary>
    /// The first address in a list of a client's addresses ("a@x.co.za; b@x.co.za"), for where only one fits,
    /// such as a Xero contact. Null when there is none.
    /// </summary>
    public static string? First(string? addresses) => Recipients.Parse(addresses).People.FirstOrDefault()?.Address;
    public IReadOnlyCollection<string> Domains => domains;
    public bool IsEmpty => addresses.Count == 0 && domains.Count == 0;

    public bool Matches(MailSummary mail) =>
        !IsEmpty && (mail.From is { } from ? [from] : Array.Empty<MailPerson>()).Concat(mail.To).Concat(mail.Cc).Any(Matches);

    /// <summary>
    /// A way to find which client a mail is about, for the badge on its row: the client the sender belongs to,
    /// else (for mail Eddie sent) the client it went to. Clients with no address or domain never match.
    /// </summary>
    public static Func<MailSummary, Client?> Finder(IEnumerable<Client> clients)
    {
        var known = clients.Select(c => (Client: c, Mail: new ClientMail(c))).Where(x => !x.Mail.IsEmpty).ToList();
        if (known.Count == 0)
            return _ => null;
        return mail =>
            (mail.From is { } from ? known.FirstOrDefault(x => x.Mail.Matches(from)).Client : null)
            ?? known.FirstOrDefault(x => mail.To.Concat(mail.Cc).Any(x.Mail.Matches)).Client;
    }

    public bool Matches(MailPerson person)
    {
        if (addresses.Contains(person.Address))
            return true;
        var at = person.Address.LastIndexOf('@');
        if (at < 0)
            return false;
        var domain = person.Address[(at + 1)..];
        return domains.Any(d => domain.Equals(d, StringComparison.OrdinalIgnoreCase) || domain.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    private void AddDomain(string domain)
    {
        domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Contains('.') && !SharedProviders.Contains(domain))
            domains.Add(domain);
    }
}
