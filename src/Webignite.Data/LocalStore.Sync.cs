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

    /// <summary>
    /// Notes that a quote, invoice, client or contact was deleted here, and when, so the deletion reaches the other PC.
    /// Only the kind, id and time are kept.
    /// </summary>
    private void MarkGone(string kind, string id) =>
        Run("INSERT INTO sync_gone (kind, id, at) VALUES ($kind, $id, $at) ON CONFLICT (kind, id) DO UPDATE SET at = $at",
            ("$kind", kind), ("$id", id), ("$at", clock.GetUtcNow().ToString("O")));

    /// <summary>Deleted here, as the other PC said, at the time it was deleted there.</summary>
    public void PutGone(string kind, string id, DateTimeOffset at) => InOneGo(() =>
    {
        Remove(kind switch { "document" => "documents", "client" => "clients", "contact" => "contacts", _ => throw new ArgumentException("Not a shared kind", nameof(kind)) }, id);
        Run("INSERT INTO sync_gone (kind, id, at) VALUES ($kind, $id, $at) ON CONFLICT (kind, id) DO UPDATE SET at = $at",
            ("$kind", kind), ("$id", id), ("$at", at.ToString("O")));
        return 0;
    });

    /// <summary>A deletion forgotten, as the record came back from the other PC changed after it.</summary>
    public void ForgetGone(string kind, string id) => Run("DELETE FROM sync_gone WHERE kind = $kind AND id = $id", ("$kind", kind), ("$id", id));

    public IReadOnlyList<(string Kind, string Id, DateTimeOffset At)> ListGone()
    {
        using var command = Command("SELECT kind, id, at FROM sync_gone");
        using var reader = command.ExecuteReader();
        var gone = new List<(string, string, DateTimeOffset)>();
        while (reader.Read())
            gone.Add((reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
        return gone;
    }

    public DateTimeOffset? GoneAt(string kind, string id) => ListGone().Where(g => g.Kind == kind && g.Id == id).Select(g => (DateTimeOffset?)g.At).FirstOrDefault();
}
