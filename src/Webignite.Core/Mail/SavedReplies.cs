using Webignite.Core.Clients;

namespace Webignite.Core.Mail;

/// <summary>Text Eddie writes often, kept on this PC and put into a mail from the editor.</summary>
public sealed record SavedReply(string Id, string Name, string Text)
{
    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>
/// Saved replies. {name} becomes the first name of the person the mail is to and {client} the client's name, so
/// "Hi {name}, thanks for the payment from {client}." reads "Hi Thandi, thanks for the payment from Karoo Coffee
/// Roasters." When a name isn't known the word is left out cleanly: "Hi {name}," becomes "Hi,".
/// </summary>
public static class SavedReplies
{
    public const string NameField = "{name}";
    public const string ClientField = "{client}";

    /// <summary>Free mail services, where sharing a domain doesn't make two people the same client.</summary>
    private static readonly HashSet<string> FreeMail = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "yahoo.com", "icloud.com", "me.com",
        "webmail.co.za", "mweb.co.za", "telkomsa.net", "vodamail.co.za", "iafrica.com", "absamail.co.za", "lantic.net",
    };

    /// <summary>The client a mail is to: one with that exact address, else one whose address or website shares the domain.</summary>
    public static Client? ClientFor(IEnumerable<Client> clients, string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@'))
            return null;
        var list = clients.ToList();
        var exact = list.FirstOrDefault(c => string.Equals(c.Email?.Trim(), address.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;
        var domain = address[(address.LastIndexOf('@') + 1)..].Trim();
        if (FreeMail.Contains(domain))
            return null;
        return list.FirstOrDefault(c =>
            (c.Email is { } email && email.Contains('@') && email[(email.LastIndexOf('@') + 1)..].Trim().Equals(domain, StringComparison.OrdinalIgnoreCase))
            || string.Equals(ClientMail.WebsiteDomain(c.Website), domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The first name to greet: the client's contact person when the mail is to the client's own address, else
    /// the first word of the name on the address. Null when there is no name, only an address.
    /// </summary>
    public static string? FirstName(MailPerson? to, Client? client)
    {
        var name = client is { ContactPerson: { Length: > 0 } contact } && (string.Equals(client.Email?.Trim(), to?.Address, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(to?.Name))
            ? contact
            : to?.Name;
        var first = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null || first.Contains('@') ? null : first.Trim(',', '"', '\'');
    }

    /// <summary>The reply's text with {name} and {client} filled in, or left out cleanly when not known.</summary>
    public static string Fill(string text, string? firstName, string? clientName)
    {
        var filled = Put(text, NameField, firstName);
        return Put(filled, ClientField, clientName);
    }

    private static string Put(string text, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return text.Replace(field, value.Trim(), StringComparison.OrdinalIgnoreCase);
        // "Hi {name}," → "Hi,"; "{name}, thanks" → "Thanks"
        var result = text.Replace(" " + field, "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace(field + ", ", "", StringComparison.OrdinalIgnoreCase).Replace(field, "", StringComparison.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>Puts text into the body where the cursor is. Returns the new body and where the cursor goes, after the text.</summary>
    public static (string Body, int Caret) Insert(string body, int caret, string text)
    {
        var at = Math.Clamp(caret, 0, body.Length);
        return (body[..at] + text + body[at..], at + text.Length);
    }

    /// <summary>What is wrong with a reply before it can be saved. Empty means fine.</summary>
    public static IReadOnlyList<string> Check(string name, string text, IEnumerable<SavedReply> existing)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
            problems.Add("Give the reply a short name, e.g. Payment received.");
        else if (existing.Any(r => r.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            problems.Add($"There is already a reply called {name.Trim()}.");
        if (string.IsNullOrWhiteSpace(text))
            problems.Add("Select the text to save first, or type it in the mail.");
        return problems;
    }
}
