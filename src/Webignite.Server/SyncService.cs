using Webignite.Core.Accounts;
using Webignite.Core.Sync;

namespace Webignite.Server;

/// <summary>Taking records in and handing them out, only ever within the caller's own workspace.</summary>
public sealed class SyncService(ServerStore store)
{
    public Result<PushReply> Push(string accountId, PushRequest request)
    {
        if (store.MembershipOf(accountId) is not { } membership)
            return Outcome.Fail<PushReply>(StatusCodes.Status403Forbidden, "You're not in a workspace.");
        var records = request.Records ?? [];
        if (records.Count > SyncLimits.RecordsPerPush)
            return Outcome.Fail<PushReply>(StatusCodes.Status400BadRequest, $"Send at most {SyncLimits.RecordsPerPush} records at a time.");
        foreach (var record in records)
        {
            if (record is null || !SyncLimits.KindIsFine(record.Kind) || !SyncLimits.IdIsFine(record.Id))
                return Outcome.Fail<PushReply>(StatusCodes.Status400BadRequest, "A record isn't one the server knows.");
            if (string.IsNullOrEmpty(record.Payload) || record.Payload.Length > SyncLimits.LargestPayload)
                return Outcome.Fail<PushReply>(StatusCodes.Status400BadRequest, "A record is empty or too big.");
        }
        // The same record twice in one go: the later copy is the one kept.
        var latest = records.GroupBy(r => (r.Kind, r.Id)).Select(g => g.Last()).ToList();
        // Each workspace has its limit, so one account can't fill the server's disk.
        if (latest.Count > 0)
        {
            var (count, bytes) = store.Usage(membership.WorkspaceId);
            if (count + latest.Count > SyncLimits.WorkspaceRecords || bytes + latest.Sum(r => (long)r.Payload!.Length) > SyncLimits.WorkspaceBytes)
                return Outcome.Fail<PushReply>(StatusCodes.Status507InsufficientStorage, "The workspace is full on the sync server. Ask whoever runs it for more room.");
        }
        var cursor = latest.Count == 0 ? store.LastRecord(membership.WorkspaceId) : store.PutRecords(membership.WorkspaceId, latest);
        return new Result<PushReply>(new PushReply(cursor));
    }

    public Result<PullReply> Pull(string accountId, PullRequest request)
    {
        if (store.MembershipOf(accountId) is not { } membership)
            return Outcome.Fail<PullReply>(StatusCodes.Status403Forbidden, "You're not in a workspace.");
        var limit = request.Limit is > 0 and <= SyncLimits.RecordsPerPull ? request.Limit : SyncLimits.RecordsPerPull;
        var since = Math.Max(0, request.Since);
        var records = store.RecordsSince(membership.WorkspaceId, since, limit + 1);
        // At most PullBytes in one reply (always at least one record), the rest on the next pull.
        var page = new List<SyncRecord>();
        long bytes = 0;
        foreach (var record in records.Take(limit))
        {
            if (page.Count > 0 && bytes + record.Payload!.Length > SyncLimits.PullBytes)
                break;
            page.Add(record);
            bytes += record.Payload!.Length;
        }
        var more = records.Count > page.Count;
        return new Result<PullReply>(new PullReply(page, page.Count > 0 ? page[^1].Seq : since, more));
    }
}
