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

    /// <summary>The largest request the server reads: the app sends at most 4 MB of records at a time, locked a third bigger.</summary>
    public const int LargestRequest = 8 * 1024 * 1024;

    /// <summary>What a pull hands back at most, so a few pulls at once can't run the server out of memory.</summary>
    public const int PullBytes = 8 * 1024 * 1024;

    /// <summary>What one workspace can keep on the server: far more than a small agency's projects need.</summary>
    public const long WorkspaceBytes = 500L * 1024 * 1024;
    public const long WorkspaceRecords = 200_000;

    public const int LongestKind = 20;

    /// <summary>
    /// A kind the server takes: small letters only. The server can't see inside records, so it doesn't need to know
    /// each kind; a newer app's kinds go through an older server.
    /// </summary>
    public static bool KindIsFine(string? kind) =>
        !string.IsNullOrEmpty(kind) && kind.Length <= LongestKind && kind.All(char.IsAsciiLetterLower);

    public static bool IdIsFine(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= LongestId && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
