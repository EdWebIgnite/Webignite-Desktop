using Webignite.Core.Documents;

namespace Webignite.Core.Clients;

/// <summary>One line of a client's details as shown on their page: what it is, and what it says.</summary>
public sealed record ClientFact(string Label, string Value);

/// <summary>A client's details as a short list to read, leaving out what hasn't been filled in.</summary>
public static class ClientFacts
{
    public static IReadOnlyList<ClientFact> Of(Client client)
    {
        var facts = new List<ClientFact>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                facts.Add(new ClientFact(label, value.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(client.Company) && client.Company.Trim() != client.Name.Trim())
            Add("On quotes and invoices", client.Name);
        Add("Contact person", client.ContactPerson);
        Add("Phone", client.Phone);
        Add("Email", client.Email);
        Add("Website", client.Website);
        Add("Address", client.Address);
        if (client.HourlyRate is { } rate)
            Add("Hourly rate", Money.Rand(rate));
        if (client.Direct)
            Add("Works with us", "Directly, not through another studio");
        return facts;
    }
}
