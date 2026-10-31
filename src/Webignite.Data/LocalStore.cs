using System.Text.Json;
using Microsoft.Data.Sqlite;
using Webignite.Core.Calendar;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;

namespace Webignite.Data;

/// <summary>
/// The app's database on this PC. Only ids, kinds, statuses and timestamps are readable in the file;
/// customers, amounts and everything else are encrypted by the <see cref="Vault"/>.
/// </summary>
public sealed class LocalStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Vault vault;
    private readonly TimeProvider clock;

    public LocalStore(string databasePath, Vault vault, TimeProvider? clock = null)
    {
        this.vault = vault;
        this.clock = clock ?? TimeProvider.System;
        if (Path.GetDirectoryName(databasePath) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        connection.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                status TEXT NOT NULL,
                created TEXT NOT NULL,
                updated TEXT NOT NULL,
                payload BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS documents_kind_updated ON documents (kind, updated DESC);
            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                auto_key TEXT UNIQUE,
                deleted INTEGER NOT NULL DEFAULT 0,
                payload BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                date TEXT NOT NULL,
                payload BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS events_date ON events (date);
            CREATE TABLE IF NOT EXISTS settings (
                name TEXT PRIMARY KEY,
                payload BLOB NOT NULL
            );
            """);
    }

    /// <summary>Opens the database in the normal place, with the normal key.</summary>
    public static LocalStore OpenDefault()
    {
        var root = DataPaths.Root();
        return new LocalStore(Path.Combine(root, "webignite.db"), new Vault(VaultKey.Load(root)));
    }

    public StoredDocument Save(StoredDocument stored)
    {
        var now = clock.GetUtcNow();
        var existing = Get(stored.Id);
        stored = stored with { Created = existing?.Created ?? now, Updated = now };

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents (id, kind, status, created, updated, payload)
            VALUES ($id, $kind, $status, $created, $updated, $payload)
            ON CONFLICT (id) DO UPDATE SET kind = $kind, status = $status, updated = $updated, payload = $payload
            """;
        command.Parameters.AddWithValue("$id", stored.Id);
        command.Parameters.AddWithValue("$kind", stored.Document.Kind.ToString());
        command.Parameters.AddWithValue("$status", stored.Status.ToString());
        command.Parameters.AddWithValue("$created", stored.Created.ToString("O"));
        command.Parameters.AddWithValue("$updated", stored.Updated.ToString("O"));
        command.Parameters.AddWithValue("$payload", Seal("document:" + stored.Id, stored));
        command.ExecuteNonQuery();
        return stored;
    }

    public StoredDocument? Get(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM documents WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is byte[] payload ? Open<StoredDocument>("document:" + id, payload) : null;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<StoredDocument> List(DocumentKind? kind = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = kind is null
            ? "SELECT id, payload FROM documents ORDER BY updated DESC"
            : "SELECT id, payload FROM documents WHERE kind = $kind ORDER BY updated DESC";
        if (kind is not null)
            command.Parameters.AddWithValue("$kind", kind.Value.ToString());
        using var reader = command.ExecuteReader();
        var list = new List<StoredDocument>();
        while (reader.Read())
            list.Add(Open<StoredDocument>("document:" + reader.GetString(0), (byte[])reader[1]));
        return list;
    }

    public TaskItem SaveTask(TaskItem task)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tasks (id, auto_key, payload) VALUES ($id, $key, $payload)
            ON CONFLICT (id) DO UPDATE SET payload = $payload, deleted = 0
            """;
        command.Parameters.AddWithValue("$id", task.Id);
        command.Parameters.AddWithValue("$key", (object?)task.AutoKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", Seal("task:" + task.Id, task));
        command.ExecuteNonQuery();
        return task;
    }

    /// <summary>
    /// Removes a task from view. Automatic tasks keep a marker so the same reminder isn't raised again.
    /// </summary>
    public void DeleteTask(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks SET deleted = 1, payload = x'' WHERE id = $id AND auto_key IS NOT NULL;
            DELETE FROM tasks WHERE id = $id AND auto_key IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TaskItem> ListTasks()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM tasks WHERE deleted = 0";
        using var reader = command.ExecuteReader();
        var list = new List<TaskItem>();
        while (reader.Read())
            list.Add(Open<TaskItem>("task:" + reader.GetString(0), (byte[])reader[1]));
        return list;
    }

    /// <summary>Every automatic-task key ever raised, including ticked-off and deleted ones.</summary>
    public IReadOnlySet<string> AutoTaskKeys()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT auto_key FROM tasks WHERE auto_key IS NOT NULL";
        using var reader = command.ExecuteReader();
        var keys = new HashSet<string>();
        while (reader.Read())
            keys.Add(reader.GetString(0));
        return keys;
    }

    /// <summary>Adds the reminders that quotes and invoices call for. Returns how many were added.</summary>
    public int RaiseAutomaticTasks(TaskSettings settings, DateOnly today)
    {
        var added = AutomaticTasks.Suggest(List(), AutoTaskKeys(), settings, today, clock.GetUtcNow());
        foreach (var task in added)
            SaveTask(task);
        return added.Count;
    }

    public CalendarEvent SaveEvent(CalendarEvent calendarEvent)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events (id, date, payload) VALUES ($id, $date, $payload)
            ON CONFLICT (id) DO UPDATE SET date = $date, payload = $payload
            """;
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.Parameters.AddWithValue("$date", calendarEvent.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$payload", Seal("event:" + calendarEvent.Id, calendarEvent));
        command.ExecuteNonQuery();
        return calendarEvent;
    }

    public void DeleteEvent(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM events WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Events from one date to another, both included. The dates are kept readable so ranges are fast.</summary>
    public IReadOnlyList<CalendarEvent> ListEvents(DateOnly from, DateOnly to)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM events WHERE date BETWEEN $from AND $to";
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        using var reader = command.ExecuteReader();
        var list = new List<CalendarEvent>();
        while (reader.Read())
            list.Add(Open<CalendarEvent>("event:" + reader.GetString(0), (byte[])reader[1]));
        return list;
    }

    public T? GetSetting<T>(string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM settings WHERE name = $name";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is byte[] payload ? Open<T>("setting:" + name, payload) : default;
    }

    public void SetSetting<T>(string name, T value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings (name, payload) VALUES ($name, $payload) ON CONFLICT (name) DO UPDATE SET payload = $payload";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$payload", Seal("setting:" + name, value));
        command.ExecuteNonQuery();
    }

    public void Dispose() => connection.Dispose();

    private byte[] Seal<T>(string context, T value) =>
        vault.Encrypt(JsonSerializer.SerializeToUtf8Bytes(value, DocumentJson.Options), context);

    private T Open<T>(string context, byte[] payload) =>
        JsonSerializer.Deserialize<T>(vault.Decrypt(payload, context), DocumentJson.Options)!;

    private void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
