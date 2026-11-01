using Webignite.Core.Clients;
using Webignite.Core.Search;

namespace Webignite.Core.Mail;

/// <summary>Someone Eddie can write to, with how likely they are to be the one meant.</summary>
public sealed record Contact(string? Name, string Address, int Score)
{
    public MailPerson Person => new(Name, Address);
    public string Display => Person.ToString();
}

/// <summary>
/// Suggestions for To, Cc and Bcc, from clients and from mail already sent and received. People Eddie has
/// written to rank above people who only wrote in; automatic senders are left out.
/// </summary>
public static class AddressBook
{
    public const int Limit = 8;

    private static readonly string[] Automatic = ["noreply", "no-reply", "donotreply", "do-not-reply", "mailer-daemon", "postmaster", "bounce", "notifications", "notification"];

    public static IReadOnlyList<Contact> Build(IEnumerable<Client> clients, IEnumerable<MailSummary> mail, IEnumerable<string> ownAddresses, DateTimeOffset now)
    {
        var own = new HashSet<string>(ownAddresses, StringComparer.OrdinalIgnoreCase);
        var scores = new Dictionary<string, (string? Name, DateTimeOffset NameDate, int Score)>(StringComparer.OrdinalIgnoreCase);

        void Add(string? name, string address, int points, DateTimeOffset when)
        {
            address = address.Trim();
            if (!Addresses.LooksValid(address) || own.Contains(address) || IsAutomatic(address))
                return;
            var useful = string.IsNullOrWhiteSpace(name) || name.Contains('@') ? null : name.Trim().Trim('"', '\'');
            if (scores.TryGetValue(address, out var known))
            {
                var keepName = useful is null || (known.Name is not null && known.NameDate > when);
                scores[address] = (keepName ? known.Name : useful, keepName ? known.NameDate : when, known.Score + points);
            }
            else
                scores[address] = (useful, useful is null ? DateTimeOffset.MinValue : when, points);
        }

        foreach (var client in clients)
        {
            foreach (var email in (client.Email ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Add(client.ContactPerson ?? client.Name, email, 50, DateTimeOffset.MinValue);
        }

        foreach (var m in mail)
        {
            var recent = now - m.Date < TimeSpan.FromDays(90) ? 2 : 1;
            if (m.From is { } from && own.Contains(from.Address))
            {
                foreach (var person in m.To.Concat(m.Cc))
                    Add(person.Name, person.Address, 10 * recent, m.Date);
            }
            else if (m.From is { } sender)
                Add(sender.Name, sender.Address, 2 * recent, m.Date);
        }

        return scores.Select(p => new Contact(p.Value.Name, p.Key, p.Value.Score))
            .OrderByDescending(c => c.Score).ThenBy(c => c.Address, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsAutomatic(string address)
    {
        var local = address[..Math.Max(0, address.IndexOf('@'))].ToLowerInvariant();
        return Automatic.Any(a => local.Contains(a, StringComparison.Ordinal));
    }

    /// <summary>The address being typed: whatever follows the last comma or semicolon.</summary>
    public static string LastPart(string? text)
    {
        text ??= "";
        var cut = text.LastIndexOfAny([',', ';']);
        return (cut < 0 ? text : text[(cut + 1)..]).Trim();
    }

    /// <summary>
    /// Contacts whose name (any word) or address starts with what's being typed, best first, leaving out
    /// anyone already in the box.
    /// </summary>
    public static IReadOnlyList<Contact> Match(IReadOnlyList<Contact> book, string? text)
    {
        var typed = QuickSearch.Fold(LastPart(text));
        if (typed.Length == 0)
            return [];
        var already = new HashSet<string>(Recipients.Parse(text).People.Select(p => p.Address), StringComparer.OrdinalIgnoreCase);
        return book.Where(c => !already.Contains(c.Address) && Matches(c, typed)).Take(Limit).ToList();
    }

    private static bool Matches(Contact contact, string typed)
    {
        var address = contact.Address.ToLowerInvariant();
        if (address.StartsWith(typed, StringComparison.Ordinal))
            return true;
        var at = address.IndexOf('@');
        if (at >= 0 && address[(at + 1)..].StartsWith(typed, StringComparison.Ordinal))
            return true;
        if (contact.Name is not { } name)
            return false;
        var folded = QuickSearch.Fold(name);
        return folded.StartsWith(typed, StringComparison.Ordinal)
            || folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(typed, StringComparison.Ordinal));
    }

    /// <summary>The box's text with the part being typed replaced by the chosen contact, ready for the next one.</summary>
    public static string Complete(string? text, string chosen)
    {
        text ??= "";
        var cut = text.LastIndexOfAny([',', ';']);
        var before = cut < 0 ? "" : text[..(cut + 1)].TrimEnd() + " ";
        return before + chosen + ", ";
    }
}
