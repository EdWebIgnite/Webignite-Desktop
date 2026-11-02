using Webignite.Core.Time;

namespace Webignite.Data;

/// <summary>Time entries. Only the date is readable in the file; who it was for and the note are encrypted.</summary>
public sealed partial class LocalStore
{
    private const string TimerSetting = "time-running";

    public TimeEntry SaveTime(TimeEntry entry)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO time_entries (id, date, payload) VALUES ($id, $date, $payload)
            ON CONFLICT (id) DO UPDATE SET date = $date, payload = $payload
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$date", entry.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$payload", Seal("time:" + entry.Id, entry));
        command.ExecuteNonQuery();
        return entry;
    }

    public void DeleteTime(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM time_entries WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Entries from one date to another, both included, newest first.</summary>
    public IReadOnlyList<TimeEntry> ListTime(DateOnly from, DateOnly to)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM time_entries WHERE date BETWEEN $from AND $to";
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        using var reader = command.ExecuteReader();
        var list = new List<TimeEntry>();
        while (reader.Read())
            list.Add(Open<TimeEntry>("time:" + reader.GetString(0), (byte[])reader[1]));
        return list.OrderByDescending(e => e.Date).ThenByDescending(e => e.Created).ToList();
    }

    /// <summary>The timer that's running, if any. It's kept in the database, so it carries on after a restart.</summary>
    public RunningTimer? RunningTimer => GetSetting<RunningTimer>(TimerSetting);

    public void SetRunningTimer(RunningTimer? timer)
    {
        if (timer is null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM settings WHERE name = $name";
            command.Parameters.AddWithValue("$name", TimerSetting);
            command.ExecuteNonQuery();
        }
        else
            SetSetting(TimerSetting, timer);
    }
}
