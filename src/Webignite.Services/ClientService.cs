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
    /// Returns the problems instead when it can't be saved, and nothing is changed.
    /// </summary>
    public (Client? Saved, IReadOnlyList<string> Problems) SaveDetails(Client edited)
    {
        var problems = ClientDirectory.Check(edited, store.ListClients());
        if (problems.Count > 0)
            return (null, problems);
        var saved = store.UpdateClient(edited.Id, current => current with
        {
            Name = edited.Name,
            ContactPerson = edited.ContactPerson,
            Email = edited.Email,
            Phone = edited.Phone,
            Website = edited.Website,
            Address = edited.Address,
            Notes = edited.Notes,
            HourlyRate = edited.HourlyRate,
        }) ?? store.SaveClient(edited with { Created = edited.Created == default ? clock.GetUtcNow() : edited.Created });
        return (saved, []);
    }

    /// <summary>Changes a client as stored now (notes, files, projects, onboarding). Null when it was deleted.</summary>
    public Client? Change(string id, Func<Client, Client> change) => store.UpdateClient(id, change);

    /// <summary>Removes the client page. Its quotes, invoices and to-dos stay as they are.</summary>
    public void Delete(string id) => store.DeleteClient(id);

    /// <summary>Puts a deleted client back, for Undo.</summary>
    public Client Restore(Client client) => store.SaveClient(client);
}
