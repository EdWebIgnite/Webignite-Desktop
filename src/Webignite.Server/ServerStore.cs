using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Webignite.Server;

/// <summary>
/// The sync server's own database: accounts, workspaces, members, invites, signed-in devices and two-factor
/// recovery codes. SQLite in one file, which suits a small server and is backed up by copying it. No customer data
/// is kept here in readable form: sign-in keys and tokens are only ever stored hashed, and two-factor secrets are
/// encrypted with the server's own key.
/// </summary>
public sealed class ServerStore
{
    private readonly string connection;

    public ServerStore(string path)
    {
        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        using var db = Open();
        Run(db, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS accounts (
                id TEXT PRIMARY KEY, email TEXT NOT NULL UNIQUE, salt TEXT NOT NULL, rounds INTEGER NOT NULL,
                key_hash BLOB NOT NULL, key_salt BLOB NOT NULL, totp TEXT, totp_pending TEXT, totp_last INTEGER NOT NULL DEFAULT -1,
                failed INTEGER NOT NULL DEFAULT 0, locked_until TEXT, recovery_wrapped TEXT NOT NULL, recovery_hash TEXT NOT NULL, created TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS workspaces (id TEXT PRIMARY KEY, name TEXT NOT NULL, created TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS members (workspace_id TEXT NOT NULL, account_id TEXT NOT NULL, role TEXT NOT NULL, wrapped_key TEXT NOT NULL,
                PRIMARY KEY (workspace_id, account_id));
            CREATE TABLE IF NOT EXISTS invites (code_hash TEXT PRIMARY KEY, workspace_id TEXT NOT NULL, created_by TEXT NOT NULL,
                expires TEXT NOT NULL, used_by TEXT, wrapped_key TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, account_id TEXT NOT NULL, device TEXT NOT NULL,
                access_hash TEXT NOT NULL UNIQUE, access_expires TEXT NOT NULL, refresh_hash TEXT NOT NULL UNIQUE,
                refresh_expires TEXT NOT NULL, previous_refresh_hash TEXT, created TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS sessions_previous ON sessions (previous_refresh_hash);
            CREATE TABLE IF NOT EXISTS recovery_codes (account_id TEXT NOT NULL, code_hash TEXT NOT NULL, used INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (account_id, code_hash));
            """);
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connection);
        db.Open();
        return db;
    }

    private static void Run(SqliteConnection db, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(db, sql, values);
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, (string Name, object? Value)[] values)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private void Run(string sql, params (string Name, object? Value)[] values)
    {
        using var db = Open();
        Run(db, sql, values);
    }

    private List<T> Read<T>(string sql, Func<SqliteDataReader, T> row, params (string Name, object? Value)[] values)
    {
        using var db = Open();
        using var command = Command(db, sql, values);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(row(reader));
        return rows;
    }

    private static string When(DateTimeOffset at) => at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset When(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    // Accounts

    public sealed record Account(string Id, string Email, string Salt, int Rounds, byte[] KeyHash, byte[] KeySalt, string? Totp,
        string? TotpPending, long TotpLast, int Failed, DateTimeOffset? LockedUntil, string RecoveryWrapped, string RecoveryHash);

    private const string AccountColumns = "id, email, salt, rounds, key_hash, key_salt, totp, totp_pending, totp_last, failed, locked_until, recovery_wrapped, recovery_hash";

    private static Account AccountRow(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
        (byte[])r[4], (byte[])r[5], Text(r, 6), Text(r, 7), r.GetInt64(8), r.GetInt32(9), Text(r, 10) is { } locked ? When(locked) : null,
        r.GetString(11), r.GetString(12));

    public Account? AccountByEmail(string email) =>
        Read($"SELECT {AccountColumns} FROM accounts WHERE email = $email", AccountRow, ("$email", email)).FirstOrDefault();

    public Account? AccountById(string id) =>
        Read($"SELECT {AccountColumns} FROM accounts WHERE id = $id", AccountRow, ("$id", id)).FirstOrDefault();

    /// <summary>A new account and, in the same go, either its new workspace (as owner) or its place in an invited one.</summary>
    public bool AddAccount(Account account, string workspaceId, string? newWorkspaceName, string role, string? inviteHash, string wrappedKey, DateTimeOffset now)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        try
        {
            Run(db, $"INSERT INTO accounts ({AccountColumns}, created) VALUES ($id, $email, $salt, $rounds, $hash, $keysalt, NULL, NULL, -1, 0, NULL, $rw, $rh, $now)",
                ("$id", account.Id), ("$email", account.Email), ("$salt", account.Salt), ("$rounds", account.Rounds),
                ("$hash", account.KeyHash), ("$keysalt", account.KeySalt), ("$rw", account.RecoveryWrapped), ("$rh", account.RecoveryHash), ("$now", When(now)));
            if (newWorkspaceName is not null)
                Run(db, "INSERT INTO workspaces (id, name, created) VALUES ($id, $name, $now)", ("$id", workspaceId), ("$name", newWorkspaceName), ("$now", When(now)));
            if (inviteHash is not null)
            {
                using var take = Command(db, "UPDATE invites SET used_by = $account WHERE code_hash = $hash AND used_by IS NULL AND expires > $now",
                    [("$account", account.Id), ("$hash", inviteHash), ("$now", When(now))]);
                if (take.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
            }
            Run(db, "INSERT INTO members (workspace_id, account_id, role, wrapped_key) VALUES ($w, $a, $role, $key)",
                ("$w", workspaceId), ("$a", account.Id), ("$role", role), ("$key", wrappedKey));
            transaction.Commit();
            return true;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) // the email is already taken
        {
            transaction.Rollback();
            return false;
        }
    }

    public void SignInFailed(string id, int failed, DateTimeOffset? lockedUntil) =>
        Run("UPDATE accounts SET failed = $failed, locked_until = $locked WHERE id = $id",
            ("$failed", failed), ("$locked", lockedUntil is { } at ? When(at) : null), ("$id", id));

    public void SignInWorked(string id, long? totpStep) =>
        Run("UPDATE accounts SET failed = 0, locked_until = NULL, totp_last = CASE WHEN $step IS NULL THEN totp_last ELSE $step END WHERE id = $id",
            ("$step", totpStep), ("$id", id));

    public void SetTotpPending(string id, string? sealedSecret) =>
        Run("UPDATE accounts SET totp_pending = $secret WHERE id = $id", ("$secret", sealedSecret), ("$id", id));

    /// <summary>Two-factor on (with its recovery codes, hashed) or off (and the codes gone).</summary>
    public void SetTotp(string id, string? sealedSecret, long lastStep, IEnumerable<string> recoveryHashes)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Run(db, "UPDATE accounts SET totp = $secret, totp_pending = NULL, totp_last = $last WHERE id = $id",
            ("$secret", sealedSecret), ("$last", lastStep), ("$id", id));
        Run(db, "DELETE FROM recovery_codes WHERE account_id = $id", ("$id", id));
        foreach (var hash in recoveryHashes)
            Run(db, "INSERT INTO recovery_codes (account_id, code_hash) VALUES ($id, $hash)", ("$id", id), ("$hash", hash));
        transaction.Commit();
    }

    /// <summary>Uses up a recovery code; false when it isn't one of the account's unused codes.</summary>
    public bool UseRecoveryCode(string id, string hash)
    {
        using var db = Open();
        using var command = Command(db, "UPDATE recovery_codes SET used = 1 WHERE account_id = $id AND code_hash = $hash AND used = 0", [("$id", id), ("$hash", hash)]);
        return command.ExecuteNonQuery() == 1;
    }

    // Workspaces

    public sealed record Membership(string WorkspaceId, string Name, string Role, string WrappedKey);

    public Membership? MembershipOf(string accountId) =>
        Read("SELECT w.id, w.name, m.role, m.wrapped_key FROM members m JOIN workspaces w ON w.id = m.workspace_id WHERE m.account_id = $a ORDER BY w.created LIMIT 1",
            r => new Membership(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)), ("$a", accountId)).FirstOrDefault();

    public IReadOnlyList<(string Email, string Role)> Members(string workspaceId) =>
        Read("SELECT a.email, m.role FROM members m JOIN accounts a ON a.id = m.account_id WHERE m.workspace_id = $w ORDER BY a.created",
            r => (r.GetString(0), r.GetString(1)), ("$w", workspaceId));

    public sealed record Invite(string WorkspaceId, string WorkspaceName, string WrappedKey);

    public Invite? OpenInvite(string hash, DateTimeOffset now) =>
        Read("SELECT i.workspace_id, w.name, i.wrapped_key FROM invites i JOIN workspaces w ON w.id = i.workspace_id WHERE i.code_hash = $hash AND i.used_by IS NULL AND i.expires > $now",
            r => new Invite(r.GetString(0), r.GetString(1), r.GetString(2)), ("$hash", hash), ("$now", When(now))).FirstOrDefault();

    public void AddInvite(string hash, string workspaceId, string createdBy, DateTimeOffset expires, string wrappedKey) =>
        Run("INSERT INTO invites (code_hash, workspace_id, created_by, expires, wrapped_key) VALUES ($hash, $w, $by, $expires, $key)",
            ("$hash", hash), ("$w", workspaceId), ("$by", createdBy), ("$expires", When(expires)), ("$key", wrappedKey));

    /// <summary>A new password after recovery: the sign-in key's hash, the member's locked key, and every device signed out.</summary>
    public void ResetPassword(string accountId, string salt, int rounds, byte[] keyHash, byte[] keySalt, string wrappedKey)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Run(db, "UPDATE accounts SET salt = $salt, rounds = $rounds, key_hash = $hash, key_salt = $keysalt, failed = 0, locked_until = NULL WHERE id = $id",
            ("$salt", salt), ("$rounds", rounds), ("$hash", keyHash), ("$keysalt", keySalt), ("$id", accountId));
        Run(db, "UPDATE members SET wrapped_key = $key WHERE account_id = $id", ("$key", wrappedKey), ("$id", accountId));
        Run(db, "DELETE FROM sessions WHERE account_id = $id", ("$id", accountId));
        transaction.Commit();
    }

    // Signed-in devices

    public sealed record Session(string Id, string AccountId, string Device, DateTimeOffset AccessExpires, DateTimeOffset RefreshExpires);

    private static Session SessionRow(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), When(r.GetString(3)), When(r.GetString(4)));

    public void AddSession(Session session, string accessHash, string refreshHash, DateTimeOffset now) =>
        Run("""
            INSERT INTO sessions (id, account_id, device, access_hash, access_expires, refresh_hash, refresh_expires, created)
            VALUES ($id, $account, $device, $access, $accessExpires, $refresh, $refreshExpires, $now)
            """,
            ("$id", session.Id), ("$account", session.AccountId), ("$device", session.Device), ("$access", accessHash),
            ("$accessExpires", When(session.AccessExpires)), ("$refresh", refreshHash), ("$refreshExpires", When(session.RefreshExpires)), ("$now", When(now)));

    public Session? SessionByAccess(string accessHash) =>
        Read("SELECT id, account_id, device, access_expires, refresh_expires FROM sessions WHERE access_hash = $h", SessionRow, ("$h", accessHash)).FirstOrDefault();

    public Session? SessionByRefresh(string refreshHash) =>
        Read("SELECT id, account_id, device, access_expires, refresh_expires FROM sessions WHERE refresh_hash = $h", SessionRow, ("$h", refreshHash)).FirstOrDefault();

    /// <summary>The device whose refresh token before this one is the given one: a sign that an old token was taken.</summary>
    public Session? SessionByOldRefresh(string refreshHash) =>
        Read("SELECT id, account_id, device, access_expires, refresh_expires FROM sessions WHERE previous_refresh_hash = $h", SessionRow, ("$h", refreshHash)).FirstOrDefault();

    /// <summary>New tokens for a device; false when the refresh token was used meanwhile (by another request).</summary>
    public bool RotateSession(string id, string oldRefreshHash, string accessHash, DateTimeOffset accessExpires, string refreshHash, DateTimeOffset refreshExpires)
    {
        using var db = Open();
        using var command = Command(db, """
            UPDATE sessions SET access_hash = $access, access_expires = $accessExpires, refresh_hash = $refresh,
                refresh_expires = $refreshExpires, previous_refresh_hash = $old
            WHERE id = $id AND refresh_hash = $old
            """,
            [("$access", accessHash), ("$accessExpires", When(accessExpires)), ("$refresh", refreshHash), ("$refreshExpires", When(refreshExpires)),
                ("$old", oldRefreshHash), ("$id", id)]);
        return command.ExecuteNonQuery() == 1;
    }

    public void RemoveSession(string id) => Run("DELETE FROM sessions WHERE id = $id", ("$id", id));

    public void RemoveSessionsOf(string accountId) => Run("DELETE FROM sessions WHERE account_id = $a", ("$a", accountId));
}
