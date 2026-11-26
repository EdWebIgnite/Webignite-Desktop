using Webignite.Core.Documents;

namespace Webignite.Core.Clients;

/// <summary>
/// Changes to a client's details that Claude or ChatGPT suggested, waiting on the client's page until Eddie accepts
/// them. Only the details given are suggested; an empty string suggests clearing that detail. Nothing changes on the
/// client until a change is accepted.
/// </summary>
public sealed record ClientSuggestion
{
    /// <summary>Claude or ChatGPT.</summary>
    public required string Who { get; init; }
    public DateTimeOffset At { get; init; }

    /// <summary>The assistant added the client itself: only the name is real until the rest is accepted.</summary>
    public bool NewClient { get; init; }

    public string? Name { get; init; }
    public string? ContactPerson { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Website { get; init; }
    public string? Address { get; init; }
    public string? Notes { get; init; }
    public decimal? HourlyRate { get; init; }
}

/// <summary>One suggested change, as shown: which detail, what it says now and what it would say.</summary>
public sealed record ClientChange(string Field, string Label, string Before, string After);

public static class ClientSuggestions
{
    public const string Name = "name", ContactPerson = "contact", Email = "email", Phone = "phone", Website = "website",
        Address = "address", Notes = "notes", HourlyRate = "rate";

    /// <summary>A newer suggestion laid over one still waiting: what it gives wins, the rest of the older one stays.</summary>
    public static ClientSuggestion Merge(ClientSuggestion? waiting, ClientSuggestion incoming) => waiting is null ? incoming : incoming with
    {
        NewClient = waiting.NewClient || incoming.NewClient,
        Name = incoming.Name ?? waiting.Name,
        ContactPerson = incoming.ContactPerson ?? waiting.ContactPerson,
        Email = incoming.Email ?? waiting.Email,
        Phone = incoming.Phone ?? waiting.Phone,
        Website = incoming.Website ?? waiting.Website,
        Address = incoming.Address ?? waiting.Address,
        Notes = incoming.Notes ?? waiting.Notes,
        HourlyRate = incoming.HourlyRate ?? waiting.HourlyRate,
    };

    /// <summary>What would change on the client, leaving out what already says the same.</summary>
    public static IReadOnlyList<ClientChange> Changes(Client client, ClientSuggestion suggestion)
    {
        var changes = new List<ClientChange>();
        void Add(string field, string label, string? now, string? suggested)
        {
            if (suggested is null)
                return;
            var (before, after) = ((now ?? "").Trim(), suggested.Trim());
            if (before != after)
                changes.Add(new ClientChange(field, label, before, after));
        }
        Add(Name, "Name", client.Name, suggestion.Name);
        Add(ContactPerson, "Contact person", client.ContactPerson, suggestion.ContactPerson);
        Add(Email, "Email", client.Email, suggestion.Email);
        Add(Phone, "Phone", client.Phone, suggestion.Phone);
        Add(Website, "Website", client.Website, suggestion.Website);
        Add(Address, "Address", client.Address, suggestion.Address);
        Add(Notes, "Notes", client.Notes, suggestion.Notes);
        if (suggestion.HourlyRate is { } rate && rate != client.HourlyRate)
            changes.Add(new ClientChange(HourlyRate, "Hourly rate", client.HourlyRate is { } was ? Money.Rand(was) : "", Money.Rand(rate)));
        return changes;
    }

    /// <summary>The client with the given suggested changes made (all of them when none are named).</summary>
    public static Client Apply(Client client, ClientSuggestion suggestion, IReadOnlyCollection<string>? fields = null)
    {
        bool Take(string field) => fields is null || fields.Contains(field);
        static string? Value(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return client with
        {
            Name = Take(Name) && Value(suggestion.Name ?? "") is { } name ? name : client.Name,
            ContactPerson = Take(ContactPerson) && suggestion.ContactPerson is { } person ? Value(person) : client.ContactPerson,
            Email = Take(Email) && suggestion.Email is { } email ? Value(email) : client.Email,
            Phone = Take(Phone) && suggestion.Phone is { } phone ? Value(phone) : client.Phone,
            Website = Take(Website) && suggestion.Website is { } website ? Value(website) : client.Website,
            Address = Take(Address) && suggestion.Address is { } address ? Value(address) : client.Address,
            Notes = Take(Notes) && suggestion.Notes is { } notes ? Value(notes) : client.Notes,
            HourlyRate = Take(HourlyRate) && suggestion.HourlyRate is { } rate ? rate : client.HourlyRate,
        };
    }

    /// <summary>What is still waiting once these changes are dealt with (accepted or turned down); null when nothing is.</summary>
    public static ClientSuggestion? Without(Client client, ClientSuggestion suggestion, IReadOnlyCollection<string> fields)
    {
        var left = suggestion with
        {
            Name = fields.Contains(Name) ? null : suggestion.Name,
            ContactPerson = fields.Contains(ContactPerson) ? null : suggestion.ContactPerson,
            Email = fields.Contains(Email) ? null : suggestion.Email,
            Phone = fields.Contains(Phone) ? null : suggestion.Phone,
            Website = fields.Contains(Website) ? null : suggestion.Website,
            Address = fields.Contains(Address) ? null : suggestion.Address,
            Notes = fields.Contains(Notes) ? null : suggestion.Notes,
            HourlyRate = fields.Contains(HourlyRate) ? null : suggestion.HourlyRate,
        };
        return Changes(client, left).Count == 0 ? null : left;
    }

    /// <summary>The line at the top of the suggested changes: who suggested them, and whether they added the client.</summary>
    public static string Heading(ClientSuggestion suggestion, int count) => suggestion.NewClient
        ? $"{suggestion.Who} added this client. Check the details before they're used."
        : count == 1 ? $"{suggestion.Who} suggests a change. Nothing changes until you accept it."
        : $"{suggestion.Who} suggests {count} changes. Nothing changes until you accept them.";
}
