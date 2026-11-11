using System.Text.Json;
using Webignite.Core.Projects;
using Webignite.Core.Sync;

namespace Webignite.Services;

// Names: each PC sends its own person with the name chosen there, so the other PC shows the same name and initials.
public sealed partial class SyncService
{
    private List<Out> OutgoingPerson()
    {
        if (store.GetSetting<TeamSettings>(TeamName)?.Me is not { } me || PeopleRules.IsDefault(me) || !SyncLimits.IdIsFine(me.Id))
            return [];
        var email = account.Account?.Email ?? me.Email;
        return [Make(SyncKinds.Person, me.Id, me with { Email = email })];
    }

    private Taken TakeInPerson(string id, string json, string fingerprint, List<(string, string, string)> sent)
    {
        if (JsonSerializer.Deserialize<Teammate>(json, RecordLock.Json) is not { } person || person.Id != id)
            return Taken.Unreadable;
        sent.Add((SyncKinds.Person, id, fingerprint));
        var team = store.GetSetting<TeamSettings>(TeamName) ?? new TeamSettings();
        var taken = PeopleRules.TakeIn(team, person);
        if (ReferenceEquals(taken, team))
            return Taken.Same;
        store.SetSetting(TeamName, taken);
        return Taken.Changed;
    }
}
