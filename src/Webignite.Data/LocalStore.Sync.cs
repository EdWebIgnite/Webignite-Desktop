namespace Webignite.Data;

/// <summary>
/// What this PC last sent to the sync server, as a fingerprint per record, so only records that changed since go
/// again. Only fingerprints are kept here, never content.
/// </summary>
public sealed partial class LocalStore
{
    public IReadOnlyDictionary<(string Kind, string Id), string> SentFingerprints()
    {
        using var command = Command("SELECT kind, id, fingerprint FROM sync_sent");
        using var reader = command.ExecuteReader();
        var sent = new Dictionary<(string, string), string>();
        while (reader.Read())
            sent[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        return sent;
    }

    public void MarkSent(IEnumerable<(string Kind, string Id, string Fingerprint)> records) => InOneGo(() =>
    {
        foreach (var (kind, id, fingerprint) in records)
            Run("INSERT INTO sync_sent (kind, id, fingerprint) VALUES ($kind, $id, $f) ON CONFLICT (kind, id) DO UPDATE SET fingerprint = $f",
                ("$kind", kind), ("$id", id), ("$f", fingerprint));
        return 0;
    });

    /// <summary>Forgets what was sent, for a new workspace: everything shared goes again.</summary>
    public void ForgetSent() => Run("DELETE FROM sync_sent");
}
