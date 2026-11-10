using Webignite.Core.Clients;

namespace Webignite.Data;

/// <summary>People in Contacts. Everything but the id is encrypted, the client they're with included.</summary>
public sealed partial class LocalStore
{
    private const string ContactContext = "contact:";

    public Person SaveContact(Person contact) => Put("contacts", ContactContext, contact.Id, contact);

    public Person? GetContact(string id) => Find<Person>("contacts", ContactContext, id);

    public void DeleteContact(string id) => InOneGo(() =>
    {
        Remove("contacts", id);
        MarkGone("contact", id);
        return 0;
    });

    public IReadOnlyList<Person> ListContacts() => ReadAll<Person>(ContactContext, "SELECT id, payload FROM contacts");
}
