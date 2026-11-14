using Webignite.Core.Clients;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Reading and saving clients, for every page. A change is made to the client as it is stored now, so two
/// parts of a client page (the details, the notes, the projects, onboarding) can't overwrite each other.
/// </summary>
public sealed class ClientService(LocalStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    /// <summary>All clients, A to Z.</summary>
    public IReadOnlyList<Client> List() => store.ListClients();

    public Client? Get(string id) => store.GetClient(id);

    /// <summary>The client with this name, matched the way names always are.</summary>
    public Client? Find(string? name) => ClientDirectory.Find(store.ListClients(), name);

    /// <summary>A new client with just a name, e.g. one already on quotes and invoices.</summary>
    public Client Add(string name) =>
        store.SaveClient(new Client { Id = Client.NewId(), Name = name.Trim(), Created = clock.GetUtcNow() });

    /// <summary>
    /// Saves what was typed on the client page: name and contact details, notes and the hourly rate. Only those
    /// fields are taken from <paramref name="edited"/>; notes, files, projects and onboarding stay as stored.
    /// With <paramref name="loaded"/> (the client as the page showed it), only the fields typed over are taken,
    /// so a change made meanwhile elsewhere (a number changed in Contacts) isn't put back.
    /// Returns the problems instead when it can't be saved, and nothing is changed.
    /// </summary>
    public (Client? Saved, IReadOnlyList<string> Problems) SaveDetails(Client edited, Client? loaded = null)
    {
        var problems = ClientDirectory.Check(edited, store.ListClients());
        if (problems.Count > 0)
            return (null, problems);
        T Typed<T>(Func<Client, T> field, Client current) =>
            loaded is not null && EqualityComparer<T>.Default.Equals(field(edited), field(loaded)) ? field(current) : field(edited);
        var saved = store.UpdateClient(edited.Id, current => current with
        {
            Name = Typed(c => c.Name, current),
            Company = Typed(c => c.Company, current),
            Direct = Typed(c => c.Direct, current),
            ContactPerson = Typed(c => c.ContactPerson, current),
            Email = Typed(c => c.Email, current),
            Phone = Typed(c => c.Phone, current),
            Website = Typed(c => c.Website, current),
            Address = Typed(c => c.Address, current),
            Notes = Typed(c => c.Notes, current),
            HourlyRate = Typed(c => c.HourlyRate, current),
        }) ?? store.SaveClient(edited with { Created = edited.Created == default ? clock.GetUtcNow() : edited.Created });
        KeepPeopleInStep(saved);
        return (saved, []);
    }

    /// <summary>Raised when a client page's details change a person in Contacts, so the Contacts page catches up.</summary>
    public event Action? PeopleChanged;

    // The contact person's name and number, and the invoice address, typed on the client page go to the people
    // they belong to in Contacts.
    private void KeepPeopleInStep(Client client)
    {
        var changed = false;
        if (client.MainContactId is { } mainId && store.GetContact(mainId) is { } main
            && ContactRules.FromClient(main, client) is var updatedMain && updatedMain != main)
        {
            store.SaveContact(updatedMain with { Changed = clock.GetUtcNow() });
            changed = true;
        }
        if (client.BillingContactId is { } billingId && store.GetContact(billingId) is { } billing
            && ContactRules.BillingFromClient(billing, client) is var updatedBilling && updatedBilling != billing)
        {
            store.SaveContact(updatedBilling with { Changed = clock.GetUtcNow() });
            changed = true;
        }
        if (changed)
            PeopleChanged?.Invoke();
    }

    /// <summary>Changes a client as stored now (notes, files, projects, onboarding). Null when it was deleted.</summary>
    public Client? Change(string id, Func<Client, Client> change) => store.UpdateClient(id, change);

    /// <summary>
    /// Removes the client page and its site logins. Its quotes, invoices and to-dos stay as they are. Returns the
    /// logins that went, so Undo can put them back with the client.
    /// </summary>
    public IReadOnlyList<SiteLogin> Delete(string id) => store.DeleteClientWithLogins(id);

    /// <summary>Puts a deleted client back with its site logins, for Undo.</summary>
    public Client Restore(Client client, IEnumerable<SiteLogin>? logins = null)
    {
        foreach (var login in logins ?? [])
            store.SaveSiteLogin(login with { ClientId = client.Id });
        return store.SaveClient(client);
    }

    /// <summary>Every client's site logins, for Ctrl+K.</summary>
    public IReadOnlyList<SiteLogin> AllLogins() => store.ListSiteLogins();

    /// <summary>A client's site logins, A to Z.</summary>
    public IReadOnlyList<SiteLogin> Logins(string clientId) => SiteLogins.Ordered(store.ListSiteLogins(clientId));

    /// <summary>
    /// Saves a login as typed, new or changed. Returns the problems instead when it can't be saved, and nothing
    /// is changed. A login whose client has gone meanwhile isn't saved.
    /// </summary>
    public (SiteLogin? Saved, IReadOnlyList<string> Problems) SaveLogin(SiteLogin typed)
    {
        var login = SiteLogins.Tidy(typed);
        var problems = SiteLogins.Check(login);
        if (problems.Count > 0)
            return (null, problems);
        if (store.GetClient(login.ClientId) is null)
            return (null, ["This client was deleted, so the login can't be kept."]);
        return (store.SaveSiteLogin(login with { Changed = clock.GetUtcNow() }), []);
    }

    /// <summary>Removes a login. Returns it, for Undo, or null when it was already gone.</summary>
    public SiteLogin? DeleteLogin(string id)
    {
        var gone = store.GetSiteLogin(id);
        if (gone is not null)
            store.DeleteSiteLogin(id);
        return gone;
    }

    /// <summary>Puts a deleted login back, for Undo, unless its client has gone too.</summary>
    public SiteLogin? RestoreLogin(SiteLogin login) => store.GetClient(login.ClientId) is null ? null : store.SaveSiteLogin(login);
}
