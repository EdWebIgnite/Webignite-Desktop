using Webignite.Core.Sync;

namespace Webignite.Server;

/// <summary>
/// The workspace's synced records. The server can't read them (they're locked on the PCs with the workspace key);
/// it only keeps the newest copy of each, numbered in the order they arrived so each PC can ask for what's new.
/// </summary>
public sealed partial class ServerStore
{
    /// <summary>Keeps the records as the newest copies, each numbered after everything before it. Returns the last number.</summary>
    public long PutRecords(string workspaceId, IReadOnlyList<SyncRecord> records)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(); // IMMEDIATE, so two pushes can't take the same numbers
        long seq;
        using (var last = Command(db, "SELECT COALESCE(MAX(seq), 0) FROM records WHERE workspace_id = $w", [("$w", workspaceId)]))
            seq = (long)last.ExecuteScalar()!;
        foreach (var record in records)
        {
            seq++;
            Run(db, """
                INSERT INTO records (workspace_id, kind, id, seq, payload) VALUES ($w, $kind, $id, $seq, $payload)
                ON CONFLICT (workspace_id, kind, id) DO UPDATE SET seq = $seq, payload = $payload
                """, ("$w", workspaceId), ("$kind", record.Kind), ("$id", record.Id), ("$seq", seq), ("$payload", record.Payload));
        }
        transaction.Commit();
        return seq;
    }

    /// <summary>Records changed after a number, oldest first, up to a limit.</summary>
    public IReadOnlyList<SyncRecord> RecordsSince(string workspaceId, long since, int limit) =>
        Read("SELECT kind, id, payload, seq FROM records WHERE workspace_id = $w AND seq > $since ORDER BY seq LIMIT $limit",
            r => new SyncRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)),
            ("$w", workspaceId), ("$since", since), ("$limit", limit));

    public long LastRecord(string workspaceId) =>
        Read("SELECT COALESCE(MAX(seq), 0) FROM records WHERE workspace_id = $w", r => r.GetInt64(0), ("$w", workspaceId)).First();
}
