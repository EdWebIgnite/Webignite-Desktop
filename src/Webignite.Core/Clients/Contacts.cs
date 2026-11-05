using Webignite.Core.Mail;

namespace Webignite.Core.Clients;

/// <summary>An email address or phone number with what it's for, e.g. "Work" or "Accounts".</summary>
public sealed record ContactWay(string Label, string Value);

/// <summary>
/// A person: someone at a client (the owner, the accounts person), a supplier, anyone mailed often. A person can
/// belong to one client, and a client can have several people; the client's contact person and the one who gets
/// quotes and invoices are two of them.
/// </summary>
public sealed record Person
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Company { get; init; }
    public string? Role { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<ContactWay> Emails { get; init; } = [];
    public IReadOnlyList<ContactWay> Phones { get; init; } = [];

    /// <summary>The client this person is with, if any.</summary>
    public string? ClientId { get; init; }

    /// <summary>Groups the person is in, e.g. "Hosting clients", usable in To, Cc and Bcc.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Changed { get; init; }

    /// <summary>The first email address, which mail goes to.</summary>
    public string? Email => Emails.FirstOrDefault()?.Value;

    /// <summary>The first phone number.</summary>
    public string? Phone => Phones.FirstOrDefault()?.Value;

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>Keeping contacts in order, and in step with the clients they belong to.</summary>
public static class ContactRules
{
    public static readonly IReadOnlyList<string> EmailLabels = ["Work", "Accounts", "Personal", "Other"];
    public static readonly IReadOnlyList<string> PhoneLabels = ["Mobile", "Work", "Home", "Other"];

    /// <summary>What's wrong before a contact can be saved; empty when it's fine.</summary>
    public static IReadOnlyList<string> Check(Person contact)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(contact.Name))
            problems.Add("Give them a name.");
        foreach (var email in contact.Emails)
        {
            if (!Addresses.LooksValid(email.Value.Trim()))
                problems.Add($"{email.Value} doesn't look like an email address.");
        }
        return problems;
    }

    /// <summary>Trimmed, with empty addresses and numbers taken out and groups once each.</summary>
    public static Person Tidy(Person contact) => contact with
    {
        Name = contact.Name.Trim(),
        Company = Blank(contact.Company),
        Role = Blank(contact.Role),
        Notes = Blank(contact.Notes),
        Emails = [.. contact.Emails.Where(e => !string.IsNullOrWhiteSpace(e.Value)).Select(e => new ContactWay(e.Label.Trim(), e.Value.Trim()))],
        Phones = [.. contact.Phones.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => new ContactWay(p.Label.Trim(), p.Value.Trim()))],
        Groups = Groups(contact.Groups),
    };

    /// <summary>Groups typed with commas between them, once each, in the order typed.</summary>
    public static IReadOnlyList<string> Groups(IEnumerable<string> typed) =>
        [.. typed.SelectMany(g => g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .DistinctBy(g => g, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>A–Z by name.</summary>
    public static IReadOnlyList<Person> Ordered(IEnumerable<Person> contacts) =>
        [.. contacts.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Every group anyone is in, A–Z.</summary>
    public static IReadOnlyList<string> AllGroups(IEnumerable<Person> contacts) =>
        [.. contacts.SelectMany(c => c.Groups).DistinctBy(g => g, StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Whether every word typed is in the name, company, role, an address, a number, a group or the notes.</summary>
    public static bool Matches(Person contact, string? words, string? clientName = null)
    {
        if (string.IsNullOrWhiteSpace(words))
            return true;
        var haystack = string.Join(' ', new[] { contact.Name, contact.Company, contact.Role, contact.Notes, clientName }
            .Concat(contact.Emails.Select(e => e.Value)).Concat(contact.Phones.Select(p => p.Value)).Concat(contact.Groups));
        return words.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// The client as its people say: the contact person's name and number from the main contact, and the address
    /// quotes and invoices go to from whoever gets them. Anything they don't have stays as it was.
    /// </summary>
    public static Client Mirror(Client client, Person? main, Person? billing) => client with
    {
        ContactPerson = main?.Name ?? client.ContactPerson,
        Phone = main?.Phone ?? client.Phone,
        Email = billing?.Email is { } email ? WithFirst(client.Email, email) : client.Email,
    };

    /// <summary>The main contact as the client page says, when its contact person or number were changed there.</summary>
    public static Person FromClient(Person main, Client client) => main with
    {
        Name = string.IsNullOrWhiteSpace(client.ContactPerson) ? main.Name : client.ContactPerson.Trim(),
        Phones = string.IsNullOrWhiteSpace(client.Phone) || client.Phone.Trim() == main.Phone ? main.Phones
            : [new ContactWay(main.Phones.FirstOrDefault()?.Label ?? "Mobile", client.Phone.Trim()), .. main.Phones.Skip(1)],
    };

    /// <summary>Whoever gets quotes and invoices, with the client's first address put first.</summary>
    public static Person BillingFromClient(Person billing, Client client) =>
        Recipients.Parse(client.Email ?? "").People.FirstOrDefault()?.Address is { } first && !string.Equals(first, billing.Email, StringComparison.OrdinalIgnoreCase)
            ? billing with { Emails = [new ContactWay(billing.Emails.FirstOrDefault()?.Label ?? "Accounts", first), .. billing.Emails.Skip(1)] }
            : billing;

    // The client's addresses with this one first and once, so the extra addresses stay copied in.
    private static string WithFirst(string? addresses, string first)
    {
        var others = Recipients.Parse(addresses ?? "").People.Select(p => p.Address)
            .Where(a => !string.Equals(a, first, StringComparison.OrdinalIgnoreCase));
        return string.Join("; ", new[] { first }.Concat(others));
    }

    /// <summary>
    /// The person a client already has on its page, as a contact, for a client from before contacts: the contact
    /// person (or the client's name) with its addresses and number. Null when there's nothing to make one from.
    /// </summary>
    public static Person? FromClientDetails(Client client, string id, DateTimeOffset now)
    {
        var emails = Recipients.Parse(client.Email ?? "").People.Select((p, i) => new ContactWay(i == 0 ? "Accounts" : "Work", p.Address)).ToList();
        if (string.IsNullOrWhiteSpace(client.ContactPerson) && emails.Count == 0)
            return null;
        return new Person
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(client.ContactPerson) ? client.Name : client.ContactPerson.Trim(),
            Company = client.Name,
            ClientId = client.Id,
            Emails = emails,
            Phones = string.IsNullOrWhiteSpace(client.Phone) ? [] : [new ContactWay("Mobile", client.Phone.Trim())],
            Created = now,
            Changed = now,
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
