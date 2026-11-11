using System.Globalization;
using Webignite.Core.Projects;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Inbox, as Asana's: what your partner did on tasks you follow or are given, comments that mention you, and likes
/// on what you made (rules in <see cref="InboxRules"/>). Worked out from the synced projects each time; only what's
/// read and archived is kept, in the setting <c>inbox</c>.
/// </summary>
public sealed class InboxService(LocalStore store, Func<string> me, Func<TeamSettings> team, TimeProvider? clock = null)
{
    private const string StateName = "inbox";
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private HashSet<string>? announced;

    /// <summary>Read or archived marks changed, or something new came in: the badge and open Inbox pages catch up.</summary>
    public event Action? Changed;

    public InboxState State
    {
        get
        {
            var stored = store.GetSetting<InboxState>(StateName) ?? new InboxState();
            var started = InboxRules.Started(stored, clock.GetUtcNow());
            if (!ReferenceEquals(started, stored))
                store.SetSetting(StateName, started);
            return started;
        }
    }

    private void Keep(InboxState state)
    {
        store.SetSetting(StateName, state);
        Changed?.Invoke();
    }

    /// <summary>Everything in Inbox, newest first, archived ones too (see <see cref="IsArchived"/>).</summary>
    public IReadOnlyList<InboxItem> Items()
    {
        var projects = store.ListProjects().Where(p => p.Deleted is null).ToList();
        var live = projects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var people = team();
        return InboxRules.Build(me(), store.ListProjectTasks().Where(t => live.Contains(t.ProjectId)), store.ListAllComments(), store.ListAllActivity(),
            State.Since!.Value, people.NameOf, CultureInfo.CurrentCulture, store.ListAllMessages().Where(m => live.Contains(m.ProjectId)), projects);
    }

    public int Unread() => InboxRules.Unread(Items(), State);

    public void MarkRead(IEnumerable<string> keys) => Keep(InboxRules.MarkRead(State, keys));

    public void MarkAllRead() => MarkRead(Items().Select(i => i.Key));

    public void Archive(string key) => Keep(InboxRules.Archive(State, key));

    public void Unarchive(string key) => Keep(InboxRules.Unarchive(State, key));

    /// <summary>
    /// Unread items that weren't there the last time this was asked, to show as notes after a sync. The first time,
    /// everything already waiting counts as known, so starting the app doesn't bring a flood of notes.
    /// </summary>
    public IReadOnlyList<InboxItem> NewlyArrived()
    {
        var state = State;
        var done = state.Read.Concat(state.Archived).ToHashSet(StringComparer.Ordinal);
        var unread = Items().Where(i => !done.Contains(i.Key)).ToList();
        if (announced is null)
        {
            announced = [.. unread.Select(i => i.Key)];
            return [];
        }
        var fresh = unread.Where(i => announced.Add(i.Key)).ToList();
        if (fresh.Count > 0)
            Changed?.Invoke();
        return fresh;
    }
}
