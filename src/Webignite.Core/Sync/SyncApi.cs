namespace Webignite.Core.Sync;

/// <summary>A locked record as the server keeps it: what kind, its id, the locked JSON, and its place in the workspace's order.</summary>
public sealed record SyncRecord(string Kind, string Id, string Payload, long Seq = 0);

public sealed record PushRequest(IReadOnlyList<SyncRecord>? Records);

public sealed record PushReply(long Cursor);

public sealed record PullRequest(long Since, int Limit = 0);

/// <summary>Records changed since the cursor, oldest first; More says there are others still to fetch.</summary>
public sealed record PullReply(IReadOnlyList<SyncRecord> Records, long Cursor, bool More);

/// <summary>What the server takes, so a mistake or a bad actor can't fill it up in one go.</summary>
public static class SyncLimits
{
    public const int RecordsPerPush = 500;
    public const int RecordsPerPull = 500;
    public const int LongestId = 64;
    public const int LargestPayload = 512 * 1024;

    public static bool IdIsFine(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= LongestId && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
