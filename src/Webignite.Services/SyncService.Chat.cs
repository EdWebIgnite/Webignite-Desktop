using System.Text.Json;
using Webignite.Core.Chat;
using Webignite.Core.Sync;
using Webignite.Data;

namespace Webignite.Services;

// Chat between the two of you. Messages and the parts of their files are written once and never change, so each
// goes once and is kept as it arrives. Only what hasn't been sent yet is read, as file parts can be large.
public sealed partial class SyncService
{
    private List<Out> OutgoingChat()
    {
        var sent = store.SentFingerprints();
        var all = new List<Out>();
        foreach (var id in store.ChatIds(parts: false).Where(id => !sent.ContainsKey((SyncKinds.Chat, id))))
            if (store.GetChatMessage(id) is { } message)
                all.Add(MakeStored(SyncKinds.Chat, id, message));
        foreach (var id in store.ChatIds(parts: true).Where(id => !sent.ContainsKey((SyncKinds.ChatPart, id))))
        {
            var fileId = id[..id.LastIndexOf('-')];
            if (store.ListChatParts(fileId).FirstOrDefault(p => p.Id == id) is { } part)
                all.Add(MakeStored(SyncKinds.ChatPart, id, part));
        }
        return all;
    }

    private static Out MakeStored<T>(string kind, string id, T value)
    {
        var json = JsonSerializer.Serialize(value, StoredJson.Options);
        return new Out(kind, id, json, RecordLock.Fingerprint(json));
    }

    /// <summary>
    /// My chat status, sent with every sync (its time changes each time, so it always goes); null sends nothing.
    /// Set by the app, which knows when the PC was last used.
    /// </summary>
    public Func<Presence?>? MyPresence { get; set; }

    /// <summary>Statuses from the other PC, as they arrive.</summary>
    public Action<Presence>? PresenceArrived { get; set; }

    private List<Out> OutgoingPresence() =>
        MyPresence?.Invoke() is { } mine && SyncLimits.IdIsFine(mine.PersonId) ? [MakeStored(SyncKinds.Presence, mine.PersonId, mine)] : [];

    private Taken TakeInPresence(string id, string json, string fingerprint, List<(string, string, string)> sent)
    {
        if (JsonSerializer.Deserialize<Presence>(json, StoredJson.Options) is not { } presence || presence.PersonId != id)
            return Taken.Unreadable;
        PresenceArrived?.Invoke(presence);
        sent.Add((SyncKinds.Presence, id, fingerprint));
        return Taken.Same; // a status isn't a change to the business, so pages aren't read again for it
    }

    private Taken TakeInChat(string kind, string id, string json, string fingerprint, List<(string, string, string)> sent)
    {
        if (kind == SyncKinds.Chat)
        {
            if (JsonSerializer.Deserialize<ChatMessage>(json, StoredJson.Options) is not { } message || message.Id != id)
                return Taken.Unreadable;
            var isNew = store.GetChatMessage(id) is null;
            store.SaveChatMessage(message);
            sent.Add((kind, id, fingerprint));
            return isNew ? Taken.Changed : Taken.Same;
        }
        if (JsonSerializer.Deserialize<ChatPart>(json, StoredJson.Options) is not { } part || part.Id != id)
            return Taken.Unreadable;
        // A picture's data can arrive in a later sync than its message. That is news for the chat, which is showing
        // the picture as "on its way" until it is told to read again.
        var isNewPart = !store.HasChatPart(id);
        store.SaveChatPart(part);
        sent.Add((kind, id, fingerprint));
        return isNewPart ? Taken.Changed : Taken.Same;
    }
}
