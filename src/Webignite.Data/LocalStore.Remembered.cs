namespace Webignite.Data;

/// <summary>
/// Whole lists that many pages ask for many times a second (documents, clients, tasks) are unlocked once and
/// handed out again until something in the database changes. Any write counts, from this app on any thread or
/// from the connector in its own process, so what is handed out is never older than the database.
/// Lists nobody asked for lately are let go by <see cref="LetGoOfUnused"/>.
/// </summary>
public sealed partial class LocalStore
{
    private readonly Lock rememberedGate = new();
    private readonly Dictionary<string, (long Stamp, object List, long Used)> remembered = [];
    private readonly ThreadLocal<(long Changes, long Version)> lastLook = new(() => (-1, -1));
    private readonly ThreadLocal<int> inOneGo = new();
    private long rememberedStamp;

    private IReadOnlyList<T> Remembered<T>(string key, Func<IReadOnlyList<T>> read)
    {
        // Inside a set of writes not yet finished, what is read may still be undone, so it is never kept.
        if (inOneGo.Value > 0)
            return read();
        // SQLite counts this connection's own writes, and separately notices anyone else's.
        (long, long) look;
        using (var command = Command("SELECT total_changes(), data_version FROM pragma_data_version"))
        using (var reader = command.ExecuteReader())
        {
            reader.Read();
            look = (reader.GetInt64(0), reader.GetInt64(1));
        }
        if (look != lastLook.Value)
        {
            lastLook.Value = look;
            Interlocked.Increment(ref rememberedStamp);
        }
        var stamp = Interlocked.Read(ref rememberedStamp);
        lock (rememberedGate)
        {
            if (remembered.TryGetValue(key, out var kept) && kept.Stamp == stamp)
            {
                remembered[key] = kept with { Used = Environment.TickCount64 };
                return (IReadOnlyList<T>)kept.List;
            }
        }
        var list = read();
        lock (rememberedGate)
            remembered[key] = (stamp, list, Environment.TickCount64);
        return list;
    }

    /// <summary>Frees the lists nobody has asked for in a while, so an idle app holds no copy of its data.</summary>
    public void LetGoOfUnused(TimeSpan unusedFor)
    {
        var before = Environment.TickCount64 - (long)unusedFor.TotalMilliseconds;
        lock (rememberedGate)
        {
            foreach (var key in remembered.Where(r => r.Value.Used <= before).Select(r => r.Key).ToList())
                remembered.Remove(key);
        }
    }
}
