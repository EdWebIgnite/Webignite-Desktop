namespace Webignite.Core.Clients;

/// <summary>A contact in Xero with the details a client page has, as Xero gives them.</summary>
public sealed record XeroCustomer(
    string Id,
    string Name,
    string? Email = null,
    string? ContactPerson = null,
    string? Phone = null,
    string? Address = null,
    string? Website = null,
    bool Active = true,
    bool IsCustomer = true,
    bool IsSupplier = false)
{
    /// <summary>
    /// Someone Webignite only pays, such as a hosting company: Xero marks them a supplier and never a customer.
    /// Xero only marks a customer once they've had an invoice in Xero, so a contact with neither mark is a client
    /// who simply hasn't been invoiced there yet, and comes in.
    /// </summary>
    public bool OnlySupplier => IsSupplier && !IsCustomer;
}

/// <summary>What bringing Xero's customers in would do: clients to add, and clients to fill in from a contact.</summary>
public sealed record XeroClientPlan(IReadOnlyList<XeroCustomer> ToAdd, IReadOnlyList<(string ClientId, XeroCustomer Contact)> ToFill)
{
    public bool IsEmpty => ToAdd.Count == 0 && ToFill.Count == 0;
}

/// <summary>
/// Xero's customers as clients. Xero is read, never written. A client already here is matched by its Xero
/// contact, else by name, and only its blank details are filled: what was typed here always stays. A customer
/// nobody here matches becomes a new client. Archived contacts and contacts that are only suppliers are not
/// brought in; every other contact is, invoiced in Xero yet or not.
/// </summary>
public static class XeroClients
{
    public static XeroClientPlan Plan(IReadOnlyList<Client> clients, IEnumerable<XeroCustomer> contacts)
    {
        var toAdd = new List<XeroCustomer>();
        var toFill = new List<(string, XeroCustomer)>();
        var byContact = clients.Where(c => c.XeroContactId is { Length: > 0 }).GroupBy(c => c.XeroContactId!).ToDictionary(g => g.Key, g => g.First());
        var byName = clients.Where(c => c.XeroContactId is not { Length: > 0 }).GroupBy(c => ClientDirectory.Key(c.Name)).ToDictionary(g => g.Key, g => g.First());
        var adding = new HashSet<string>();
        foreach (var contact in contacts.Where(c => c.Name.Trim().Length > 0))
        {
            var key = ClientDirectory.Key(contact.Name);
            var match = byContact.GetValueOrDefault(contact.Id) ?? byName.GetValueOrDefault(key);
            if (match is not null)
            {
                if (Fill(match, contact) != match)
                    toFill.Add((match.Id, contact));
                byName.Remove(key);
                continue;
            }
            if (contact.Active && !contact.OnlySupplier && adding.Add(key))
                toAdd.Add(contact);
        }
        return new XeroClientPlan(toAdd, toFill);
    }

    /// <summary>The client with its blank details taken from the contact, and the contact remembered.</summary>
    public static Client Fill(Client client, XeroCustomer contact) => client with
    {
        XeroContactId = client.XeroContactId is { Length: > 0 } id ? id : contact.Id,
        Email = Blank(client.Email) ? Clean(contact.Email) ?? client.Email : client.Email,
        ContactPerson = Blank(client.ContactPerson) ? Clean(contact.ContactPerson) ?? client.ContactPerson : client.ContactPerson,
        Phone = Blank(client.Phone) ? Clean(contact.Phone) ?? client.Phone : client.Phone,
        Address = Blank(client.Address) ? Clean(contact.Address) ?? client.Address : client.Address,
        Website = Blank(client.Website) ? Clean(contact.Website) ?? client.Website : client.Website,
    };

    /// <summary>A new client from a Xero customer.</summary>
    public static Client New(XeroCustomer contact, string id, DateTimeOffset now) =>
        Fill(new Client { Id = id, Name = contact.Name.Trim(), Created = now }, contact);

    /// <summary>"3 new clients from Xero, 2 filled in", or "" when nothing changed.</summary>
    public static string Summary(int added, int filled)
    {
        var parts = new List<string>();
        if (added > 0)
            parts.Add(Wording.Count(added, "new client") + " from Xero");
        if (filled > 0)
            parts.Add(Wording.Count(filled, "client") + " filled in from Xero");
        return string.Join(", ", parts);
    }

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
