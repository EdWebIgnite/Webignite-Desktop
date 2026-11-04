using System.Globalization;
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
    // One connection per thread. The app and the connector both use the store from more than one thread (a
    // lookup in the background, two assistant requests at once), and one shared connection let a write from one
    // thread land inside another thread's transaction and be rolled back with it. SQLite keeps separate
    // connections apart and makes a writer wait for the other (WAL mode, and the command timeout as busy wait).
    // Every transaction here is opened and finished in one synchronous method, so it stays on one thread.
    private readonly ThreadLocal<SqliteConnection> connections;
    private readonly string connectionString;
    private readonly Vault vault;
    private readonly TimeProvider clock;

    private SqliteConnection connection => connections.Value!;

    public LocalStore(string databasePath, Vault vault, TimeProvider? clock = null)
    {
        this.vault = vault;
        this.clock = clock ?? TimeProvider.System;
        if (Path.GetDirectoryName(databasePath) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, DefaultTimeout = 30 }.ToString();
        connections = new ThreadLocal<SqliteConnection>(OpenConnection, trackAllValues: true);
        Execute("""
            PRAGMA journal_mode = WAL;
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
            CREATE INDEX IF NOT EXISTS mail_messages_newest ON mail_messages (date DESC);
            CREATE TABLE IF NOT EXISTS mail_bodies (
                account_id TEXT NOT NULL,
                folder TEXT NOT NULL,
                uid INTEGER NOT NULL,
                payload BLOB NOT NULL,
                PRIMARY KEY (account_id, folder, uid)
            );
            CREATE TABLE IF NOT EXISTS time_entries (
                id TEXT PRIMARY KEY,
                date TEXT NOT NULL,
                payload BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS time_entries_date ON time_entries (date);
            CREATE TABLE IF NOT EXISTS settings (
                name TEXT PRIMARY KEY,
                payload BLOB NOT NULL
            );
            """);
        Upgrade();
    }

    /// <summary>The layout this version of the app writes. Bump it when a table changes shape, and add the step below.</summary>
    public const int SchemaVersion = 1;

    /// <summary>What the database file was last written by, from SQLite's own user_version slot.</summary>
    public int StoredSchemaVersion
    {
        get
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>
    /// Brings an older database up to date, one numbered step at a time. The CREATE TABLE IF NOT EXISTS block above
    /// handles new tables on its own; this is for changing a table that already has data in it, e.g. a new column or
    /// an index over a field that is now in the clear. Records themselves are JSON, so a new field needs no step.
    /// </summary>
    private void Upgrade()
    {
        var version = StoredSchemaVersion;
        if (version >= SchemaVersion)
            return;
        using var transaction = connection.BeginTransaction();
        // if (version < 2) Execute("ALTER TABLE ...");
        Execute($"PRAGMA user_version = {SchemaVersion}");
        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var opened = new SqliteConnection(connectionString);
        opened.Open();
        using var command = opened.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return opened;
    }

    /// <summary>
    /// Opens the database in the normal place, with the normal key, and checks the key fits before anything is
    /// written, so a wrong key stops here with a clear message instead of failing record by record later.
    /// </summary>
    public static LocalStore OpenDefault()
    {
        var root = DataPaths.Root();
        var store = new LocalStore(Path.Combine(root, "webignite.db"), new Vault(VaultKey.Load(root)));
        try
        {
            store.Check();
        }
        catch (System.Security.Cryptography.CryptographicException e)
        {
            store.Dispose();
            throw new InvalidOperationException(
                "Webignite's data on this PC can't be unlocked with the key on this PC. Nothing was changed. " +
                "Restore your latest backup, which carries the right key.", e);
        }
        return store;
    }

    /// <summary>Writes a consistent copy of the whole database to a new file, while the app keeps using it.</summary>
    public void CopyTo(string path)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path";
        command.Parameters.AddWithValue("$path", path);
        command.ExecuteNonQuery();
    }

    /// <summary>Unlocks a sample of every kind of record. Throws when the key doesn't match the data.</summary>
    public void Check()
    {
        var samples = new[]
        {
            ("SELECT name, payload FROM settings LIMIT 1", "setting:"),
            ("SELECT id, payload FROM documents LIMIT 1", "document:"),
            ("SELECT id, payload FROM clients LIMIT 1", "client:"),
            ("SELECT id, payload FROM tasks LIMIT 1", "task:"),
            ("SELECT id, payload FROM entries LIMIT 1", "entry:"),
            ("SELECT id, payload FROM time_entries LIMIT 1", "time:"),
            ("SELECT id, payload FROM events LIMIT 1", "event:"),
            ("SELECT id, payload FROM recurring LIMIT 1", "recurring:"),
            ("SELECT id, payload FROM retainers LIMIT 1", "retainer:"),
            ("SELECT id, payload FROM renewals LIMIT 1", "renewal:"),
            ("SELECT id, payload FROM mail_accounts LIMIT 1", "mail-account:"),
        };
        foreach (var (sql, prefix) in samples)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            if (reader.Read())
                vault.Decrypt((byte[])reader[1], prefix + reader.GetString(0));
        }
    }

    public void Dispose()
    {
        foreach (var opened in connections.Values)
            opened.Dispose();
        connections.Dispose();
    }

    private byte[] Seal<T>(string context, T value) =>
        vault.Encrypt(JsonSerializer.SerializeToUtf8Bytes(value, StoredJson.Options), context);

    private T Open<T>(string context, byte[] payload) =>
        JsonSerializer.Deserialize<T>(vault.Decrypt(payload, context), StoredJson.Options)!;

    private void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
