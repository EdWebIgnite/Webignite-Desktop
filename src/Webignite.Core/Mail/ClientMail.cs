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
        if (WebsiteDomain(client.Website) is { } site)
            AddDomain(site);
    }

    public IReadOnlyCollection<string> Addresses => addresses;
    public IReadOnlyCollection<string> Domains => domains;
    public bool IsEmpty => addresses.Count == 0 && domains.Count == 0;

    public bool Matches(MailSummary mail) =>
        !IsEmpty && (mail.From is { } from ? [from] : Array.Empty<MailPerson>()).Concat(mail.To).Concat(mail.Cc).Any(Matches);

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

    /// <summary>"https://www.karoocoffee.co.za/shop" gives "karoocoffee.co.za".</summary>
    public static string? WebsiteDomain(string? website)
    {
        if (string.IsNullOrWhiteSpace(website))
            return null;
        var text = website.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.Host.Contains('.'))
            return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
