using System.Text.Json;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Sync;
using Webignite.Data;

namespace Webignite.Services;

// Quotes and invoices, clients and people in Contacts: the business both of you run, so all of them sync, not only
// what's ticked shared. They travel as Shared<T> (the record, when it was last changed, and when it was deleted), in
// the same JSON the PC keeps them in, and are merged by SharedMerge.
public sealed partial class SyncService
{
    private static string SharedJson<T>(Shared<T> value) where T : class => JsonSerializer.Serialize(value, StoredJson.Options);

    private static Out MakeShared<T>(string kind, string id, Shared<T> value) where T : class
    {
        var json = SharedJson(value);
        return new Out(kind, id, json, RecordLock.Fingerprint(json));
    }

    /// <summary>Every quote, invoice, client and contact on this PC, and each one deleted here.</summary>
    private List<Out> OutgoingShared()
    {
        var all = new List<Out>();
        all.AddRange(store.List().Select(d => MakeShared(SyncKinds.Document, d.Id, new Shared<StoredDocument>(d, d.Updated))));
        // A client's folder and linked files are paths on this PC, so they never leave it.
        all.AddRange(store.ListClients().Select(c => MakeShared(SyncKinds.Client, c.Id, new Shared<Client>(Travelling(c), c.Updated))));
        all.AddRange(store.ListContacts().Select(p => MakeShared(SyncKinds.Contact, p.Id, new Shared<Person>(p, p.Changed))));
        foreach (var (kind, id, at) in store.ListGone())
        {
            all.Add(kind switch
            {
                SyncKinds.Document => MakeShared(kind, id, new Shared<StoredDocument>(null, at, at)),
                SyncKinds.Client => MakeShared(kind, id, new Shared<Client>(null, at, at)),
                _ => MakeShared(kind, id, new Shared<Person>(null, at, at)),
            });
        }
        return all;
    }

    private Taken TakeInShared(string kind, string id, string json, string fingerprint, List<(string, string, string)> sent) => kind switch
    {
        SyncKinds.Document => MergeShared(kind, id, json, fingerprint, sent,
            () => store.Get(id) is { } d ? new Shared<StoredDocument>(d, d.Updated) : null,
            (_, older, newer) => SharedMerge.Document(older, newer), d => store.PutAsSynced(d)),
        SyncKinds.Client => MergeShared(kind, id, json, fingerprint, sent,
            () => store.GetClient(id) is { } c ? new Shared<Client>(Travelling(c), c.Updated) : null,
            (_, older, newer) => newer, c => store.PutClientAsSynced(KeepHere(c))),
        _ => MergeShared(kind, id, json, fingerprint, sent,
            () => store.GetContact(id) is { } p ? new Shared<Person>(p, p.Changed) : null,
            (_, older, newer) => SharedMerge.Contact(older, newer), p => store.SaveContact(p)),
    };

    private Taken MergeShared<T>(string kind, string id, string json, string fingerprint, List<(string, string, string)> sent,
        Func<Shared<T>?> find, Func<T?, T, T, T> both, Action<T> save) where T : class
    {
        var remote = JsonSerializer.Deserialize<Shared<T>>(json, StoredJson.Options);
        if (remote is null || (remote.Value is null) == (remote.Deleted is null) || remote.Value is not null && IdOf(remote.Value) != id)
            return Taken.Unreadable;
        var local = find() ?? (store.GoneAt(kind, id) is { } at ? new Shared<T>(null, at, at) : null);
        var merged = local is null ? remote : SharedMerge.Merge(local, remote, (older, newer) => both(local.Value, older, newer));
        var mergedJson = SharedJson(merged);
        // What the server has now matches this PC: nothing to send back. Otherwise this PC's copy goes on the next push.
        if (RecordLock.Fingerprint(mergedJson) == fingerprint)
            sent.Add((kind, id, fingerprint));
        if (local is not null && SharedJson(local) == mergedJson)
            return Taken.Same;
        if (merged.Deleted is { } deleted)
            store.PutGone(kind, id, deleted);
        else
        {
            store.ForgetGone(kind, id);
            save(merged.Value!);
        }
        return Taken.Changed;
    }

    private static Client Travelling(Client client) => client with { Folder = null, Files = [] };

    // A client from the other PC keeps this PC's own folder and linked files.
    private Client KeepHere(Client arrived) =>
        store.GetClient(arrived.Id) is { } here ? SharedMerge.Client(here, here, arrived) : Travelling(arrived);

    private static string? IdOf(object value) => value switch
    {
        StoredDocument d => d.Id,
        Client c => c.Id,
        Person p => p.Id,
        _ => null,
    };
}
