using System.Text.Json;
using Webignite.Core.Sync;
using Webignite.Data;

namespace Webignite.Services;

// Settings that belong to the business (SharedSettings): the copy changed later wins, so a change on either PC reaches
// the other, and a partner joining gets the owner's setup.
public sealed partial class SyncService
{
    private List<Out> OutgoingSettings() =>
        [.. store.ListSharedSettings().Select(s => MakeStored(SyncKinds.Setting, s.Name, s))];

    private Taken TakeInSetting(string id, string json, string fingerprint, bool owner, List<(string, string, string)> sent)
    {
        if (JsonSerializer.Deserialize<SharedSetting>(json, StoredJson.Options) is not { } arriving || arriving.Name != id
            || !SharedSettings.IsShared(id) || arriving.Value.ValueKind == JsonValueKind.Undefined)
            return Taken.Unreadable;
        var here = store.ListSharedSettings().FirstOrDefault(s => s.Name == id);
        if (here is not null && RecordLock.Fingerprint(JsonSerializer.Serialize(here, StoredJson.Options)) == fingerprint)
        {
            sent.Add((SyncKinds.Setting, id, fingerprint));
            return Taken.Same;
        }
        if (!SharedSettings.TakeArriving(here is null ? null : here.Updated, arriving.Updated, owner))
            return Taken.Same; // this PC's copy is newer: it goes on the next push
        store.PutSettingAsSynced(id, arriving.Value, arriving.Updated);
        sent.Add((SyncKinds.Setting, id, fingerprint));
        return Taken.Changed;
    }
}
