using System.Text.Json;
using Microsoft.Data.Sqlite;
using Webignite.Core.Calendar;
using Webignite.Core.Clients;
using Webignite.Core.Finance;
using Webignite.Core.Renewals;
using Webignite.Core.Retainers;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;

namespace Webignite.Data;

/// <summary>
/// The app's database on this PC. Only ids, kinds, statuses, dates and timestamps are readable in the file;
/// customers, amounts and everything else are encrypted by the <see cref="Vault"/>.
/// </summary>
public sealed partial class LocalStore : IDisposable
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
            CREATE TABLE IF NOT EXISTS clients (
                id TEXT PRIMARY KEY,
                payload BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS entries (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                date TEXT NOT NULL,
                recurring_key TEXT UNIQUE,
                deleted INTEGER NOT NULL DEFAULT 0,
                payload BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS entries_date ON entries (date);
            CREATE TABLE IF NOT EXISTS recurring (
                id TEXT PRIMARY KEY,
                payload BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS retainers (
                id TEXT PRIMARY KEY,
                payload BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS drafts_made (
                key TEXT PRIMARY KEY,
                document_id TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS renewals (
                id TEXT PRIMARY KEY,
                payload BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mail_accounts (
                id TEXT PRIMARY KEY,
                payload BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mail_folders (
                account_id TEXT NOT NULL,
                path TEXT NOT NULL,
                payload BLOB NOT NULL,
                PRIMARY KEY (account_id, path)
            );
            CREATE TABLE IF NOT EXISTS mail_messages (
                account_id TEXT NOT NULL,
                folder TEXT NOT NULL,
                uid INTEGER NOT NULL,
                date TEXT NOT NULL,
                payload BLOB NOT NULL,
                PRIMARY KEY (account_id, folder, uid)
            );
            CREATE INDEX IF NOT EXISTS mail_messages_date ON mail_messages (account_id, folder, date DESC);
            CREATE TABLE IF NOT EXISTS mail_bodies (
                account_id TEXT NOT NULL,
                folder TEXT NOT NULL,
                uid INTEGER NOT NULL,
                payload BLOB NOT NULL,
                PRIMARY KEY (account_id, folder, uid)
            );
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

    public Client SaveClient(Client client)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO clients (id, payload) VALUES ($id, $payload) ON CONFLICT (id) DO UPDATE SET payload = $payload";
        command.Parameters.AddWithValue("$id", client.Id);
        command.Parameters.AddWithValue("$payload", Seal("client:" + client.Id, client));
        command.ExecuteNonQuery();
        return client;
    }

    public void DeleteClient(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clients WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>All clients, A to Z. Names are encrypted, so sorting happens after reading.</summary>
    public IReadOnlyList<Client> ListClients()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM clients";
        using var reader = command.ExecuteReader();
        var list = new List<Client>();
        while (reader.Read())
            list.Add(Open<Client>("client:" + reader.GetString(0), (byte[])reader[1]));
        return list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public MoneyEntry SaveEntry(MoneyEntry entry)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entries (id, kind, date, recurring_key, payload) VALUES ($id, $kind, $date, $key, $payload)
            ON CONFLICT (id) DO UPDATE SET kind = $kind, date = $date, payload = $payload, deleted = 0
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$kind", entry.Kind.ToString());
        command.Parameters.AddWithValue("$date", entry.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$key", (object?)entry.RecurringKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", Seal("entry:" + entry.Id, entry));
        command.ExecuteNonQuery();
        return entry;
    }

    /// <summary>Removes an entry. One added by a monthly expense keeps a marker so it isn't added again.</summary>
    public void DeleteEntry(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE entries SET deleted = 1, payload = x'' WHERE id = $id AND recurring_key IS NOT NULL;
            DELETE FROM entries WHERE id = $id AND recurring_key IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Entries dated from one day to another, both included, oldest first.</summary>
    public IReadOnlyList<MoneyEntry> ListEntries(DateOnly from, DateOnly to)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM entries WHERE deleted = 0 AND date >= $from AND date <= $to ORDER BY date, id";
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        using var reader = command.ExecuteReader();
        var list = new List<MoneyEntry>();
        while (reader.Read())
            list.Add(Open<MoneyEntry>("entry:" + reader.GetString(0), (byte[])reader[1]));
        return list;
    }

    public IReadOnlySet<string> RecurringKeys()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT recurring_key FROM entries WHERE recurring_key IS NOT NULL";
        using var reader = command.ExecuteReader();
        var keys = new HashSet<string>();
        while (reader.Read())
            keys.Add(reader.GetString(0));
        return keys;
    }

    public RecurringExpense SaveRecurring(RecurringExpense recurring)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO recurring (id, payload) VALUES ($id, $payload) ON CONFLICT (id) DO UPDATE SET payload = $payload";
        command.Parameters.AddWithValue("$id", recurring.Id);
        command.Parameters.AddWithValue("$payload", Seal("recurring:" + recurring.Id, recurring));
        command.ExecuteNonQuery();
        return recurring;
    }

    /// <summary>Stops a monthly expense. Entries it already added stay, as that money was spent.</summary>
    public void DeleteRecurring(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM recurring WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<RecurringExpense> ListRecurring()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM recurring";
        using var reader = command.ExecuteReader();
        var list = new List<RecurringExpense>();
        while (reader.Read())
            list.Add(Open<RecurringExpense>("recurring:" + reader.GetString(0), (byte[])reader[1]));
        return list.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds the expenses monthly items call for up to today. Returns how many were added.</summary>
    public int AddRecurringExpenses(DateOnly today)
    {
        var due = RecurringExpenses.Due(ListRecurring(), RecurringKeys(), today, clock.GetUtcNow());
        foreach (var entry in due)
            SaveEntry(entry);
        return due.Count;
    }

    public Retainer SaveRetainer(Retainer retainer)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO retainers (id, payload) VALUES ($id, $payload) ON CONFLICT (id) DO UPDATE SET payload = $payload";
        command.Parameters.AddWithValue("$id", retainer.Id);
        command.Parameters.AddWithValue("$payload", Seal("retainer:" + retainer.Id, retainer));
        command.ExecuteNonQuery();
        return retainer;
    }

    /// <summary>Stops a retainer. Invoices it already drafted stay.</summary>
    public void DeleteRetainer(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM retainers WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Retainer> ListRetainers()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM retainers";
        using var reader = command.ExecuteReader();
        var list = new List<Retainer>();
        while (reader.Read())
            list.Add(Open<Retainer>("retainer:" + reader.GetString(0), (byte[])reader[1]));
        return list.OrderBy(r => r.ClientName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every invoice the app has drafted by itself (retainer months, renewals), by key, with the draft it made.</summary>
    public IReadOnlyDictionary<string, string> DraftsMade()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, document_id FROM drafts_made";
        using var reader = command.ExecuteReader();
        var runs = new Dictionary<string, string>();
        while (reader.Read())
            runs[reader.GetString(0)] = reader.GetString(1);
        return runs;
    }

    /// <summary>
    /// Saves a draft the app made by itself, with its key, in one go: both land or neither does,
    /// so a deleted draft is never made again and a crash never makes two.
    /// </summary>
    private StoredDocument SaveAutomaticDraft(string key, Document invoice)
    {
        // Raw BEGIN so Save's own commands join in.
        Execute("BEGIN");
        try
        {
            var stored = Save(new StoredDocument { Id = StoredDocument.NewId(), Document = invoice });
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO drafts_made (key, document_id) VALUES ($key, $id)";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$id", stored.Id);
            command.ExecuteNonQuery();
            Execute("COMMIT");
            return stored;
        }
        catch
        {
            Execute("ROLLBACK");
            throw;
        }
    }

    /// <summary>
    /// Drafts the invoices retainers call for up to today. They are drafts on this PC only;
    /// nothing goes to Xero until a person sends it. Returns how many were drafted.
    /// </summary>
    public int DraftRetainerInvoices(DocumentSettings settings, DateOnly today)
    {
        var due = RetainerSchedule.Due(ListRetainers(), DraftsMade().Keys.ToHashSet(), settings, today);
        foreach (var (key, invoice) in due)
            SaveAutomaticDraft(key, invoice);
        return due.Count;
    }

    public Renewal SaveRenewal(Renewal renewal)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO renewals (id, payload) VALUES ($id, $payload) ON CONFLICT (id) DO UPDATE SET payload = $payload";
        command.Parameters.AddWithValue("$id", renewal.Id);
        command.Parameters.AddWithValue("$payload", Seal("renewal:" + renewal.Id, renewal));
        command.ExecuteNonQuery();
        return renewal;
    }

    public void DeleteRenewal(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM renewals WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>All renewals, soonest first.</summary>
    public IReadOnlyList<Renewal> ListRenewals()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM renewals";
        using var reader = command.ExecuteReader();
        var list = new List<Renewal>();
        while (reader.Read())
            list.Add(Open<Renewal>("renewal:" + reader.GetString(0), (byte[])reader[1]));
        return list.OrderBy(r => r.RenewsOn).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Adds renewal reminders to the to-do list and drafts invoices for renewals clients pay for,
    /// once each is inside its reminder window. Returns how many of each were added.
    /// </summary>
    public (int Reminders, int Invoices) RaiseRenewals(DocumentSettings settings, DateOnly today)
    {
        var renewals = ListRenewals();
        var reminders = RenewalRules.Reminders(renewals, AutoTaskKeys(), today, clock.GetUtcNow());
        foreach (var task in reminders)
            SaveTask(task);
        var invoices = RenewalRules.InvoicesDue(renewals, DraftsMade().Keys.ToHashSet(), settings, today);
        foreach (var (key, invoice) in invoices)
            SaveAutomaticDraft(key, invoice);
        return (reminders.Count, invoices.Count);
    }

    /// <summary>Marks a renewal done: moves it to the next date and adds what it cost under Money.</summary>
    public Renewal MarkRenewed(Renewal renewal, DateOnly paidOn)
    {
        var (next, expense) = RenewalRules.Renewed(renewal, paidOn, clock.GetUtcNow());
        if (expense is not null)
            SaveEntry(expense);
        return SaveRenewal(next);
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
