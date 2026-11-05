using Webignite.Core.Clients;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// People in Contacts, for the Contacts page, the client page and search. A person can be with one client; the
/// client's contact person and whoever gets quotes and invoices are two of its people, and their name, number
/// and address show on the client (<see cref="ContactRules.Mirror"/>), so a change on either side shows on the
/// other. <see cref="Changed"/> tells every open page.
/// </summary>
public sealed class ContactService(LocalStore store, TimeProvider? clock = null)
{
    private const string MadeSetting = "contacts-made";
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public event Action? Changed;

    /// <summary>Everyone, A to Z. The first time, the people already on client pages become contacts.</summary>
    public IReadOnlyList<Person> List()
    {
        MakeFromClients();
        return ContactRules.Ordered(store.ListContacts());
    }

    public Person? Get(string id) => store.GetContact(id);

    /// <summary>A client's people, A to Z.</summary>
    public IReadOnlyList<Person> ForClient(string clientId) => [.. List().Where(c => c.ClientId == clientId)];

    /// <summary>
    /// Saves a person as typed, new or changed, and puts their name, number and address on their client where
    /// they're its contact person or get its invoices. Returns the problems instead, changing nothing.
    /// </summary>
    public (Person? Saved, IReadOnlyList<string> Problems) Save(Person typed)
    {
        var contact = ContactRules.Tidy(typed);
        var problems = ContactRules.Check(contact);
        if (problems.Count > 0)
            return (null, problems);
        var before = store.GetContact(contact.Id);
        var now = clock.GetUtcNow();
        var saved = store.SaveContact(contact with { Created = before?.Created ?? (contact.Created == default ? now : contact.Created), Changed = now });
        // Moved to another client, or none: the old client stops pointing at them.
        if (before?.ClientId is { } oldClient && oldClient != saved.ClientId)
            Forget(oldClient, saved.Id);
        if (saved.ClientId is { } clientId)
            MirrorClient(clientId);
        Changed?.Invoke();
        return (saved, []);
    }

    /// <summary>Removes a person. Returns them, for Undo.</summary>
    public Person? Delete(string id)
    {
        if (store.GetContact(id) is not { } gone)
            return null;
        store.DeleteContact(id);
        if (gone.ClientId is { } clientId)
            Forget(clientId, id);
        Changed?.Invoke();
        return gone;
    }

    /// <summary>Puts a deleted person back, for Undo. Being someone's contact person isn't put back.</summary>
    public Person Restore(Person contact)
    {
        var restored = store.SaveContact(contact);
        Changed?.Invoke();
        return restored;
    }

    /// <summary>Makes this person the client's contact person: their name and number show on the client.</summary>
    public Client? MakeMain(string clientId, string contactId) => Point(clientId, c => c with { MainContactId = contactId }, contactId);

    /// <summary>Quotes and invoices for the client go to this person's address.</summary>
    public Client? MakeBilling(string clientId, string contactId) => Point(clientId, c => c with { BillingContactId = contactId }, contactId);

    /// <summary>Puts a person with a client, or with none.</summary>
    public Person? Link(string contactId, string? clientId)
    {
        if (store.GetContact(contactId) is not { } contact)
            return null;
        return Save(contact with { ClientId = clientId }).Saved;
    }

    private Client? Point(string clientId, Func<Client, Client> change, string contactId)
    {
        if (store.GetContact(contactId) is { } contact && contact.ClientId != clientId)
            store.SaveContact(contact with { ClientId = clientId, Changed = clock.GetUtcNow() });
        store.UpdateClient(clientId, change);
        var client = MirrorClient(clientId);
        Changed?.Invoke();
        return client;
    }

    // The client's contact person, number and invoice address from its people.
    private Client? MirrorClient(string clientId) => store.UpdateClient(clientId, client =>
    {
        var main = client.MainContactId is { } m ? store.GetContact(m) : null;
        var billing = client.BillingContactId is { } b ? store.GetContact(b) : null;
        var mirrored = ContactRules.Mirror(client, main, billing);
        return mirrored == client ? null : mirrored;
    });

    // A person no longer with a client stops being its contact person or getting its invoices. What they had
    // put on the client stays, as typed details.
    private void Forget(string clientId, string contactId) => store.UpdateClient(clientId, client =>
        client.MainContactId != contactId && client.BillingContactId != contactId ? null
            : client with
            {
                MainContactId = client.MainContactId == contactId ? null : client.MainContactId,
                BillingContactId = client.BillingContactId == contactId ? null : client.BillingContactId,
            });

    /// <summary>
    /// Once: each client from before Contacts with a contact person or an address gets that person as a contact,
    /// who is its contact person and gets its invoices. Clients that already have people are left alone.
    /// </summary>
    private void MakeFromClients()
    {
        if (store.GetSetting<bool>(MadeSetting))
            return;
        var now = clock.GetUtcNow();
        var withPeople = store.ListContacts().Select(c => c.ClientId).OfType<string>().ToHashSet();
        foreach (var client in store.ListClients().Where(c => !withPeople.Contains(c.Id)))
        {
            if (ContactRules.FromClientDetails(client, Person.NewId(), now) is not { } person)
                continue;
            store.SaveContact(person);
            store.UpdateClient(client.Id, c => c with { MainContactId = person.Id, BillingContactId = person.Email is null ? null : person.Id });
        }
        store.SetSetting(MadeSetting, true);
    }
}
