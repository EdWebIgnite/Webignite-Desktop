using System.Text.Json;
using Webignite.Core.Accounts;
using Webignite.Core.Projects;
using Webignite.Core.Sync;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>Where this PC is up to with the workspace on the sync server.</summary>
public sealed record SyncState
{
    public string WorkspaceId { get; init; } = "";
    public long Cursor { get; init; }
    public DateTimeOffset? LastSynced { get; init; }

    /// <summary>The owner's ids for the two of you, once they've arrived (on the partner's PC).</summary>
    public TeamRecord? Team { get; init; }
}

/// <summary>How a sync went: what came in, what went out, and anything that stopped it or was left out.</summary>
public sealed record SyncResult(int Changed = 0, int Sent = 0, string? Problem = null, int Unreadable = 0, bool WaitingForOwner = false, bool Skipped = false, int TooBig = 0)
{
    public bool Worked => Problem is null && !Skipped;

    /// <summary>What to tell the person after they asked to sync now.</summary>
    public string Summary()
    {
        if (Problem is not null)
            return Problem;
        if (Skipped)
            return "A sync is already running.";
        var parts = new List<string>
        {
            (Changed, Sent) switch
            {
                (0, 0) => "Synced. Nothing new either way.",
                (_, 0) => $"Synced. {Things(Changed)} came in from your partner.",
                (0, _) => $"Synced. {Things(Sent)} went to your partner.",
                _ => $"Synced. {Things(Changed)} came in and {Things(Sent).ToLowerInvariant()} went out.",
            },
        };
        if (WaitingForOwner)
            parts.Add("Your own changes go out once the workspace owner's PC has synced, so both PCs know who's who.");
        if (Unreadable > 0)
            parts.Add($"{Things(Unreadable)} from the server couldn't be unlocked and were left out.");
        if (TooBig > 0)
            parts.Add($"{Things(TooBig)} are too big to sync and stayed on this PC.");
        return string.Join(" ", parts);
    }

    private static string Things(int count) => count == 1 ? "One change" : $"{count} changes";
}

/// <summary>
/// Syncs shared projects (their sections, tasks, comments, activity and messages), and every quote, invoice, client and
/// contact (SyncService.Shared.cs), with the workspace on the sync server.
/// Every record is locked on this PC with the workspace key before it goes, so the server only ever holds what it
/// can't read. What comes in is merged field by field with this PC's copy (see <see cref="RecordMerge"/>), and only
/// records that changed since they were last sent go out. Personal projects never leave the PC.
/// </summary>
public sealed partial class SyncService(LocalStore store, AccountClient account, TimeProvider? clock = null) : IDisposable
{
    private const string StateName = "sync";
    private const string TeamName = "team";
    private const string TeamId = "team";
    private const int PagesAtMost = 200;
    private const string TakenBackMark = "taken-back:";
    private const int SendAtOnce = 200;
    private const int BytesAtOnce = 4 * 1024 * 1024;

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim one = new(1, 1);

    public void Dispose() => one.Dispose();

    public SyncState State => store.GetSetting<SyncState>(StateName) ?? new SyncState();

    private void Keep(SyncState state) => store.SetSetting(StateName, state);

    public Task<SyncResult> SyncAsync(CancellationToken cancel = default) => SyncAsync(quick: false, cancel);

    /// <summary>
    /// Takes in whatever your partner sent, then sends what changed here. A quick sync (every minute) sends only chat
    /// and your status; the rest goes with a full one (every 15 minutes, by hand, and on closing).
    /// </summary>
    public async Task<SyncResult> SyncAsync(bool quick, CancellationToken cancel = default)
    {
        if (!account.IsSignedIn)
            return new SyncResult(Skipped: true);
        if (!await one.WaitAsync(0, cancel))
            return new SyncResult(Skipped: true);
        try
        {
            return await Run(quick, cancel);
        }
        finally
        {
            one.Release();
        }
    }

    private async Task<SyncResult> Run(bool quick, CancellationToken cancel)
    {
        if (account.WorkspaceKey is not { } workspaceKey)
            return new SyncResult(Problem: "Sign in again on this PC, so it has the key that unlocks your shared projects.");
        if (account.Account is null && await account.RefreshAccountAsync(cancel) is { Worked: false } refreshed)
            return new SyncResult(Problem: refreshed.Problem);
        if (account.Account is not { } signedIn)
            return new SyncResult(Problem: "Sign in again.");
        var workspace = signedIn.Workspace;
        var state = State;
        if (state.WorkspaceId != workspace.Id)
        {
            store.ForgetSent();
            state = new SyncState { WorkspaceId = workspace.Id };
            Keep(state);
        }
        var key = RecordLock.KeyFrom(workspaceKey);
        var owner = workspace.Role == AccountRoles.Owner;

        // In first: what your partner changed, merged with what's here.
        var changed = 0;
        var unreadable = 0;
        for (var page = 0; page < PagesAtMost; page++)
        {
            var reply = await account.PullAsync(state.Cursor, cancel);
            if (reply.Value is not { } pulled)
                return new SyncResult(changed, Problem: reply.Problem, Unreadable: unreadable);
            var sent = new List<(string, string, string)>();
            var sentBefore = store.SentFingerprints();
            // The team first, so ids are changed over before the records that use them are merged.
            foreach (var record in pulled.Records.OrderBy(r => r.Kind == SyncKinds.Team ? 0 : 1))
            {
                switch (TakeIn(record, key, owner, sentBefore, ref state, sent))
                {
                    case Taken.Changed: changed++; break;
                    case Taken.Unreadable: unreadable++; break;
                }
            }
            store.MarkSent(sent);
            state = state with { Cursor = pulled.Cursor };
            Keep(state);
            if (!pulled.More)
                break;
        }

        // Then out: what changed here since it was last sent.
        // The partner's PC waits for the owner's ids before sending, so nothing goes out under ids nobody else knows.
        if (!owner && state.Team is null)
        {
            Keep(state with { LastSynced = clock.GetUtcNow() });
            return new SyncResult(changed, Unreadable: unreadable, WaitingForOwner: true);
        }
        TeamRecord? teamSent = null;
        var outgoing = quick ? OutgoingQuick() : Outgoing(owner && state.Team is null, out teamSent);
        var already = store.SentFingerprints();
        var changedHere = outgoing.Where(r => !already.TryGetValue((r.Kind, r.Id), out var f) || f != r.Fingerprint).ToList();
        // Locked, a record grows by a third; one too big for the server stays here rather than holding up the rest.
        var due = changedHere.Where(r => (System.Text.Encoding.UTF8.GetByteCount(r.Json) + 30) / 3 * 4 <= SyncLimits.LargestPayload).ToList();
        var tooBig = changedHere.Count - due.Count;
        // What's taken back is worked out from everything sent before, so only a full sync can tell.
        var takenBack = quick ? [] : TakenBack(outgoing, already);
        due.AddRange(takenBack);
        var count = 0;
        foreach (var batch in Batches(due))
        {
            var locked = batch.Select(r => new SyncRecord(r.Kind, r.Id, RecordLock.Seal(r.Kind, r.Id, r.Json, key))).ToList();
            var reply = await account.PushAsync(locked, cancel);
            if (!reply.Worked)
                return new SyncResult(changed, count, reply.Problem, unreadable, TooBig: tooBig);
            store.MarkSent(batch.Select(r => (r.Kind, r.Id, takenBack.Contains(r) ? TakenBackMark + r.Fingerprint : r.Fingerprint)));
            count += batch.Count;
            if (teamSent is not null && batch.Any(r => r.Kind == SyncKinds.Team))
            {
                state = state with { Team = teamSent };
                Keep(state);
            }
        }
        Keep(state with { LastSynced = clock.GetUtcNow() });
        return new SyncResult(changed, count, Unreadable: unreadable, TooBig: tooBig);
    }

    private enum Taken { Same, Changed, Unreadable }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, RecordLock.Json);

    private Taken TakeIn(SyncRecord record, byte[] key, bool owner, IReadOnlyDictionary<(string Kind, string Id), string> sentBefore,
        ref SyncState state, List<(string, string, string)> sent)
    {
        if (RecordLock.Open(record.Kind, record.Id, record.Payload, key) is not { } json)
            return Taken.Unreadable;
        try
        {
            var fingerprint = RecordLock.Fingerprint(json);
            // What this PC sent itself, coming back: nothing to take in.
            if (sentBefore.TryGetValue((record.Kind, record.Id), out var mine) && (mine == fingerprint || mine == TakenBackMark + fingerprint))
                return Taken.Same;
            switch (record.Kind)
            {
                case SyncKinds.Project:
                    return Merge(json, record.Id, fingerprint, sent, SyncKinds.Project, store.GetProject, MergeProject, p => store.SaveProject(p));
                case SyncKinds.Heading:
                    return Merge(json, record.Id, fingerprint, sent, SyncKinds.Heading, store.GetHeading, RecordMerge.Merge, h => store.SaveHeading(h));
                case SyncKinds.Task:
                    return Merge(json, record.Id, fingerprint, sent, SyncKinds.Task, store.GetProjectTask, RecordMerge.Merge, t => store.SaveProjectTask(t));
                case SyncKinds.Comment:
                    return Merge(json, record.Id, fingerprint, sent, SyncKinds.Comment, store.GetComment, RecordMerge.Merge, c => store.SaveComment(c));
                case SyncKinds.Message:
                    return Merge(json, record.Id, fingerprint, sent, SyncKinds.Message, store.GetMessage, RecordMerge.Merge, m => store.SaveMessage(m));
                case SyncKinds.Activity:
                    var activity = JsonSerializer.Deserialize<TaskActivity>(json, RecordLock.Json);
                    if (activity?.Id != record.Id)
                        return Taken.Unreadable;
                    store.SaveActivity(activity); // written once: a copy already here is the same line
                    sent.Add((SyncKinds.Activity, record.Id, fingerprint));
                    return Taken.Same;
                case SyncKinds.Chat or SyncKinds.ChatPart:
                    return TakeInChat(record.Kind, record.Id, json, fingerprint, sent);
                case SyncKinds.Presence:
                    return TakeInPresence(record.Id, json, fingerprint, sent);
                case SyncKinds.Setting:
                    return TakeInSetting(record.Id, json, fingerprint, owner, sent);
                case SyncKinds.Document or SyncKinds.Client or SyncKinds.Contact:
                    return TakeInShared(record.Kind, record.Id, json, fingerprint, sent);
                case SyncKinds.Team:
                    if (record.Id != TeamId || JsonSerializer.Deserialize<TeamRecord>(json, RecordLock.Json) is not { } team)
                        return Taken.Unreadable;
                    var changed = TakeOnTeam(team, owner);
                    state = state with { Team = team };
                    sent.Add((SyncKinds.Team, TeamId, fingerprint));
                    return changed ? Taken.Changed : Taken.Same;
                default:
                    return Taken.Same; // a kind from a newer version of the app
            }
        }
        catch (JsonException)
        {
            return Taken.Unreadable;
        }
    }

    /// <summary>
    /// A project merged as any record, except that one your partner stopped sharing goes from this PC (marked deleted
    /// as of their change, so sharing it again brings it back); one you made yourself stays, as your own.
    /// </summary>
    private Project MergeProject(Project local, Project remote)
    {
        var merged = RecordMerge.Merge(local, remote);
        var me = store.GetSetting<TeamSettings>(TeamName)?.Me?.Id;
        return !merged.Shared && merged.Deleted is null && merged.Created.By != me ? merged with { Deleted = merged.Changed } : merged;
    }

    private static Taken Merge<T>(string json, string id, string fingerprint, List<(string, string, string)> sent, string kind,
        Func<string, T?> find, Func<T, T, T> merge, Action<T> save) where T : class, ITracked
    {
        var remote = JsonSerializer.Deserialize<T>(json, RecordLock.Json);
        if (remote is null || remote.Id != id)
            return Taken.Unreadable;
        var local = find(id);
        var merged = local is null ? remote : merge(local, remote);
        var mergedFingerprint = RecordLock.Fingerprint(Json(merged));
        // What the server has now matches this PC: nothing to send back. Otherwise this PC's copy goes on the next push.
        if (mergedFingerprint == fingerprint)
            sent.Add((kind, id, fingerprint));
        if (local is not null && RecordLock.Fingerprint(Json(local)) == mergedFingerprint)
            return Taken.Same;
        save(merged);
        return Taken.Changed;
    }

    /// <summary>
    /// Takes on the shared ids for the two of you (on the partner's PC, or another of the owner's PCs), and changes
    /// this PC's records over to them.
    /// </summary>
    private bool TakeOnTeam(TeamRecord shared, bool owner)
    {
        var stored = store.GetSetting<TeamSettings>(TeamName) ?? new TeamSettings();
        var team = ProjectRules.Ensure(stored, null);
        var (me, partner) = owner ? (shared.Owner, shared.Member) : (shared.Member, shared.Owner);
        var map = TeamIds.Map(team, me, partner);
        var next = TeamIds.TakeOn(team, me, partner);
        if (next != stored)
            store.SetSetting(TeamName, next);
        if (map.Count == 0)
            return next != stored;
        foreach (var project in store.ListProjects())
            Remapped(project, map, p => store.SaveProject(p));
        foreach (var heading in store.ListHeadings())
            Remapped(heading, map, h => store.SaveHeading(h));
        foreach (var task in store.ListProjectTasks())
            Remapped(task, map, t => store.SaveProjectTask(t));
        foreach (var comment in store.ListAllComments())
            Remapped(comment, map, c => store.SaveComment(c));
        foreach (var message in store.ListAllMessages())
            Remapped(message, map, m => store.SaveMessage(m));
        foreach (var activity in store.ListAllActivity())
            Remapped(activity, map, store.ReplaceActivity);
        return true;
    }

    private static void Remapped<T>(T record, IReadOnlyDictionary<string, string> map, Action<T> save)
    {
        var json = Json(record);
        var changed = TeamIds.Remap(json, map);
        if (changed != json)
            save(JsonSerializer.Deserialize<T>(changed, RecordLock.Json)!);
    }

    private sealed record Out(string Kind, string Id, string Json, string Fingerprint);

    private static Out Make<T>(string kind, string id, T value)
    {
        var json = Json(value);
        return new Out(kind, id, json, RecordLock.Fingerprint(json));
    }

    /// <summary>A quick sync's records: chat and my status only.</summary>
    private List<Out> OutgoingQuick() => [.. OutgoingChat().Concat(OutgoingPresence()).Where(r => SyncLimits.IdIsFine(r.Id))];

    /// <summary>
    /// Everything in shared projects (deleted ones too, so the deletion reaches the other PC), and the team when this
    /// is the first of the owner's PCs to sync.
    /// </summary>
    private List<Out> Outgoing(bool withTeam, out TeamRecord? team)
    {
        var projects = store.ListProjects().Where(p => p.Shared).ToList();
        var shared = projects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var all = new List<Out>();
        team = null;
        if (withTeam)
        {
            var stored = store.GetSetting<TeamSettings>(TeamName) ?? new TeamSettings();
            var made = ProjectRules.Ensure(stored, null);
            if (made != stored)
                store.SetSetting(TeamName, made); // made now, so the ids sent are the ones kept
            team = new TeamRecord(made.Me!, made.Partner!);
            all.Add(Make(SyncKinds.Team, TeamId, team));
        }
        all.AddRange(projects.Select(p => Make(SyncKinds.Project, p.Id, p)));
        all.AddRange(store.ListHeadings().Where(h => shared.Contains(h.ProjectId)).Select(h => Make(SyncKinds.Heading, h.Id, h)));
        all.AddRange(store.ListProjectTasks().Where(t => shared.Contains(t.ProjectId)).Select(t => Make(SyncKinds.Task, t.Id, t)));
        all.AddRange(store.ListAllComments().Where(c => shared.Contains(c.ProjectId)).Select(c => Make(SyncKinds.Comment, c.Id, c)));
        all.AddRange(store.ListAllMessages().Where(m => shared.Contains(m.ProjectId)).Select(m => Make(SyncKinds.Message, m.Id, m)));
        all.AddRange(store.ListAllActivity().Where(a => shared.Contains(a.ProjectId)).Select(a => Make(SyncKinds.Activity, a.Id, a)));
        all.AddRange(OutgoingShared());
        all.AddRange(OutgoingChat());
        all.AddRange(OutgoingPresence());
        all.AddRange(OutgoingSettings());
        return [.. all.Where(r => SyncLimits.IdIsFine(r.Id))];
    }

    /// <summary>
    /// What was sent before and isn't shared any more, sent once more so the other PC knows. A project no longer
    /// shared goes as it is, marked not shared, and nothing more of it follows; the other PC lets it go unless that
    /// person made it. A task moved into a personal project goes as a copy marked deleted, with nothing in it. The
    /// tasks of a project no longer shared aren't sent again. Sharing a project again, or moving a task back,
    /// changes it later than that, so it comes back in full.
    /// </summary>
    private List<Out> TakenBack(List<Out> outgoing, IReadOnlyDictionary<(string Kind, string Id), string> already)
    {
        var going = outgoing.Select(r => (r.Kind, r.Id)).ToHashSet();
        var stamp = new Stamp(store.GetSetting<TeamSettings>(TeamName)?.Me?.Id ?? "", clock.GetUtcNow());
        var back = new List<Out>();
        foreach (var ((kind, id), fingerprint) in already)
        {
            if (fingerprint.StartsWith(TakenBackMark, StringComparison.Ordinal) || going.Contains((kind, id)))
                continue;
            if (kind == SyncKinds.Project && store.GetProject(id) is { Deleted: null, Shared: false } project)
                back.Add(Make(kind, id, project));
            else if (kind == SyncKinds.Task && store.GetProjectTask(id) is { Deleted: null } task && !already.ContainsKey((SyncKinds.Project, task.ProjectId)))
                // Its move is stamped, so the other PC takes the new project too and doesn't send its own copy back.
                back.Add(Make(kind, id, new ProjectTask
                {
                    Id = id, ProjectId = task.ProjectId, Created = task.Created, Changed = stamp, Deleted = stamp,
                    Fields = new Dictionary<string, Stamp> { ["project"] = stamp },
                }));
        }
        return back;
    }

    private static IEnumerable<List<Out>> Batches(List<Out> due)
    {
        var batch = new List<Out>();
        var bytes = 0;
        foreach (var record in due)
        {
            if (batch.Count > 0 && (batch.Count >= SendAtOnce || bytes + record.Json.Length > BytesAtOnce))
            {
                yield return batch;
                batch = [];
                bytes = 0;
            }
            batch.Add(record);
            bytes += record.Json.Length;
        }
        if (batch.Count > 0)
            yield return batch;
    }
}
