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
            if (record is null || !SyncKinds.All.Contains(record.Kind ?? "") || !SyncLimits.IdIsFine(record.Id))
                return Outcome.Fail<PushReply>(StatusCodes.Status400BadRequest, "A record isn't one the server knows.");
            if (string.IsNullOrEmpty(record.Payload) || record.Payload.Length > SyncLimits.LargestPayload)
                return Outcome.Fail<PushReply>(StatusCodes.Status400BadRequest, "A record is empty or too big.");
        }
        // The same record twice in one go: the later copy is the one kept.
        var latest = records.GroupBy(r => (r.Kind, r.Id)).Select(g => g.Last()).ToList();
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
        var more = records.Count > limit;
        var page = more ? records.Take(limit).ToList() : records;
        return new Result<PullReply>(new PullReply(page, page.Count > 0 ? page[^1].Seq : since, more));
    }
}
