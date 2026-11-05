using Webignite.Core.Clients;
using Webignite.Core.Search;

namespace Webignite.Core.Mail;

/// <summary>
/// Someone Eddie can write to, with how likely they are to be the one meant; or a group from Contacts, which
/// puts all its people in the box at once.
/// </summary>
public sealed record Contact(string? Name, string Address, int Score, IReadOnlyList<MailPerson>? Members = null)
{
    public MailPerson Person => new(Name, Address);
    public bool IsGroup => Members is not null;
    public string Display => Members is { } members ? $"{Name} (group of {members.Count})" : Person.ToString();
}

/// <summary>
/// Suggestions for To, Cc and Bcc: people in Contacts first, then clients and mail already sent and received.
/// People Eddie has written to rank above people who only wrote in; automatic senders are left out. Groups in
/// Contacts are offered too.
/// </summary>
public static class AddressBook
{
    public const int Limit = 8;

    private static readonly string[] Automatic = ["noreply", "no-reply", "donotreply", "do-not-reply", "mailer-daemon", "postmaster", "bounce", "notifications", "notification"];

    public static IReadOnlyList<Contact> Build(IEnumerable<Client> clients, IEnumerable<MailSummary> mail, IEnumerable<string> ownAddresses, DateTimeOffset now,
        IEnumerable<Person>? people = null)
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

        // A name typed in Contacts beats one read off a mail.
        var known = (people ?? []).ToList();
        foreach (var person in known)
            foreach (var email in person.Emails)
                Add(person.Name, email.Value, 200, DateTimeOffset.MaxValue);

        foreach (var client in clients)
        {
            foreach (var person in Recipients.Parse(client.Email).People)
                Add(client.ContactPerson ?? client.Name, person.Address, 50, DateTimeOffset.MinValue);
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

        var everyone = scores.Select(p => new Contact(p.Value.Name, p.Key, p.Value.Score))
            .OrderByDescending(c => c.Score).ThenBy(c => c.Address, StringComparer.OrdinalIgnoreCase);
        return [.. Groups(known), .. everyone];
    }

    /// <summary>Each group in Contacts with the people in it who have an address, A–Z.</summary>
    public static IReadOnlyList<Contact> Groups(IEnumerable<Person> people)
    {
        var list = people.ToList();
        return [.. ContactRules.AllGroups(list).Select(group => new Contact(group, "group:" + group, 1000,
                [.. list.Where(p => p.Email is not null && p.Groups.Contains(group, StringComparer.CurrentCultureIgnoreCase))
                    .Select(p => new MailPerson(p.Name, p.Email!))]))
            .Where(g => g.Members!.Count > 0)];
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
        return book.Where(c => c.Members is { } members ? members.Any(m => !already.Contains(m.Address)) : !already.Contains(c.Address))
            .Where(c => Matches(c, typed)).Take(Limit).ToList();
    }

    /// <summary>
    /// The box's text with these people added at the end (a person picked from Contacts, or a group), anyone
    /// already in the box left out, and whatever was half typed kept.
    /// </summary>
    public static string Add(string? text, IEnumerable<MailPerson> people)
    {
        var current = (text ?? "").Trim().TrimEnd(',', ';').Trim();
        var already = new HashSet<string>(Recipients.Parse(current).People.Select(p => p.Address), StringComparer.OrdinalIgnoreCase);
        var adding = people.Where(p => already.Add(p.Address)).Select(p => p.ToString()).ToList();
        if (adding.Count == 0)
            return text ?? "";
        return (current.Length > 0 ? current + ", " : "") + string.Join(", ", adding) + ", ";
    }

    /// <summary>
    /// The Contacts button's list in a mail: groups first, then everyone in Contacts with an address, A–Z, narrowed by
    /// what's typed (name, company, client, group, address or phone).
    /// </summary>
    public static IReadOnlyList<Contact> Pick(IEnumerable<Person> people, IReadOnlyDictionary<string, string> clientNames, string? search)
    {
        var list = people.ToList();
        var words = QuickSearch.Fold(search ?? "").Trim();
        var groups = Groups(list).Where(g => words.Length == 0 || QuickSearch.Fold(g.Name ?? "").Contains(words, StringComparison.Ordinal));
        var everyone = list
            .Where(p => p.Email is not null)
            .Where(p => ContactRules.Matches(p, search, p.ClientId is { } id && clientNames.TryGetValue(id, out var name) ? name : null))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new Contact(p.Name, p.Email!, 1000));
        return [.. groups, .. everyone];
    }

    /// <summary>The box's text with what was typed replaced by the person chosen, or by everyone in a group not already in it.</summary>
    public static string Insert(string? text, Contact chosen)
    {
        if (chosen.Members is not { } members)
            return Complete(text, chosen.Display);
        var already = new HashSet<string>(Recipients.Parse(text).People.Select(p => p.Address), StringComparer.OrdinalIgnoreCase);
        return Complete(text, string.Join(", ", members.Where(m => !already.Contains(m.Address)).Select(m => m.ToString())));
    }

    private static bool Matches(Contact contact, string typed)
    {
        if (contact.IsGroup)
            return QuickSearch.Fold(contact.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(typed, StringComparison.Ordinal));
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
