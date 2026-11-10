using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Webignite.Server;

/// <summary>
/// The sync server's own database: accounts, workspaces, members, invites, signed-in devices and two-factor
/// recovery codes. SQLite in one file, which suits a small server and is backed up by copying it. No customer data
/// is kept here in readable form: sign-in keys and tokens are only ever stored hashed, and two-factor secrets are
/// encrypted with the server's own key.
/// </summary>
public sealed partial class ServerStore
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
            CREATE TABLE IF NOT EXISTS records (workspace_id TEXT NOT NULL, kind TEXT NOT NULL, id TEXT NOT NULL, seq INTEGER NOT NULL,
                payload TEXT NOT NULL, PRIMARY KEY (workspace_id, kind, id));
            CREATE INDEX IF NOT EXISTS records_order ON records (workspace_id, seq);
            """);
        // Columns added after the first version: each added once, to a database made before it.
        AddColumn(db, "workspaces", "plan", "TEXT NOT NULL DEFAULT 'trial'");
        AddColumn(db, "workspaces", "paid_until", "TEXT");
        AddColumn(db, "invites", "secret_check", "TEXT");
        // Every refresh token a device has used, so any old one used again signs that device out.
        Run(db, "CREATE TABLE IF NOT EXISTS used_refresh (hash TEXT PRIMARY KEY, session_id TEXT NOT NULL)");
        // Wrong tries counted per account and address, so someone guessing from one place can't lock the owner out
        // everywhere else.
        Run(db, """
            CREATE TABLE IF NOT EXISTS sign_in_failures (account_id TEXT NOT NULL, address TEXT NOT NULL, failed INTEGER NOT NULL DEFAULT 0,
                locked_until TEXT, PRIMARY KEY (account_id, address))
            """);
    }

    /// <summary>A whole, consistent copy of the database in a new file, taken while the server runs.</summary>
    public void CopyTo(string path)
    {
        using var db = Open();
        Run(db, "VACUUM INTO $path", ("$path", path));
    }

    private static void AddColumn(SqliteConnection db, string table, string column, string definition)
    {
        using var look = Command(db, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name", [("$name", column)]);
        if ((long)look.ExecuteScalar()! == 0)
            Run(db, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
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

    /// <summary>
    /// One more wrong try, counted in the database itself so tries sent at the same moment all count; the account is
    /// locked until <paramref name="lockedUntil"/> on the <paramref name="limit"/>th.
    /// </summary>
    public void SignInFailed(string id, int limit, DateTimeOffset lockedUntil) =>
        Run("""
            UPDATE accounts SET
                locked_until = CASE WHEN failed + 1 >= $limit THEN $locked ELSE locked_until END,
                failed = CASE WHEN failed + 1 >= $limit THEN 0 ELSE failed + 1 END
            WHERE id = $id
            """, ("$limit", limit), ("$locked", When(lockedUntil)), ("$id", id));

    /// <summary>
    /// One more wrong try from an address, counted in the database itself. That address is locked out of this
    /// account until <paramref name="lockedUntil"/> on the <paramref name="limit"/>th.
    /// </summary>
    public void FailedFrom(string id, string address, int limit, DateTimeOffset lockedUntil) =>
        Run("""
            INSERT INTO sign_in_failures (account_id, address, failed, locked_until) VALUES ($id, $address, 1, NULL)
            ON CONFLICT (account_id, address) DO UPDATE SET
                locked_until = CASE WHEN failed + 1 >= $limit THEN $locked ELSE locked_until END,
                failed = CASE WHEN failed + 1 >= $limit THEN 0 ELSE failed + 1 END
            """, ("$id", id), ("$address", address), ("$limit", limit), ("$locked", When(lockedUntil)));

    /// <summary>Until when an address is locked out of an account, if it is.</summary>
    public DateTimeOffset? LockedFrom(string id, string address) =>
        Read("SELECT locked_until FROM sign_in_failures WHERE account_id = $id AND address = $address",
            r => Text(r, 0) is { } at ? When(at) : (DateTimeOffset?)null, ("$id", id), ("$address", address)).FirstOrDefault();

    /// <summary>A sign-in from an address worked: its wrong tries are forgotten.</summary>
    public void ClearFailuresFrom(string id, string address) =>
        Run("DELETE FROM sign_in_failures WHERE account_id = $id AND address = $address", ("$id", id), ("$address", address));

    /// <summary>
    /// A sign-in that worked. With a two-factor code's time step, false when that step (or a later one) was used
    /// already, by a sign-in at the same moment, so one code can't be used twice.
    /// </summary>
    public bool SignInWorked(string id, long? totpStep)
    {
        using var db = Open();
        using var command = Command(db, """
            UPDATE accounts SET failed = 0, locked_until = NULL, totp_last = CASE WHEN $step IS NULL THEN totp_last ELSE $step END
            WHERE id = $id AND ($step IS NULL OR totp_last < $step)
            """, [("$step", totpStep), ("$id", id)]);
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>New workspaces made since a time, for the daily limit on sign-ups.</summary>
    public int WorkspacesSince(DateTimeOffset since) =>
        Read("SELECT COUNT(*) FROM workspaces WHERE created > $since", r => r.GetInt32(0), ("$since", When(since))).First();

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

    /// <summary>An open invite; <see cref="SecretCheck"/> is the hash of the check made from its secret half.</summary>
    public sealed record Invite(string WorkspaceId, string WorkspaceName, string WrappedKey, string? SecretCheck);

    public Invite? OpenInvite(string hash, DateTimeOffset now) =>
        Read("SELECT i.workspace_id, w.name, i.wrapped_key, i.secret_check FROM invites i JOIN workspaces w ON w.id = i.workspace_id WHERE i.code_hash = $hash AND i.used_by IS NULL AND i.expires > $now",
            r => new Invite(r.GetString(0), r.GetString(1), r.GetString(2), Text(r, 3)), ("$hash", hash), ("$now", When(now))).FirstOrDefault();

    public void AddInvite(string hash, string workspaceId, string createdBy, DateTimeOffset expires, string wrappedKey, string secretCheckHash) =>
        Run("INSERT INTO invites (code_hash, workspace_id, created_by, expires, wrapped_key, secret_check) VALUES ($hash, $w, $by, $expires, $key, $check)",
            ("$hash", hash), ("$w", workspaceId), ("$by", createdBy), ("$expires", When(expires)), ("$key", wrappedKey), ("$check", secretCheckHash));

    /// <summary>
    /// A new password after recovery: the sign-in key's hash, the member's locked key, a new recovery key (the old one
    /// stops working), and every device signed out.
    /// </summary>
    public void ResetPassword(string accountId, string salt, int rounds, byte[] keyHash, byte[] keySalt, string wrappedKey, string recoveryWrapped, string recoveryHash)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Run(db, """
            UPDATE accounts SET salt = $salt, rounds = $rounds, key_hash = $hash, key_salt = $keysalt, failed = 0, locked_until = NULL,
                recovery_wrapped = $rw, recovery_hash = $rh
            WHERE id = $id
            """,
            ("$salt", salt), ("$rounds", rounds), ("$hash", keyHash), ("$keysalt", keySalt), ("$rw", recoveryWrapped), ("$rh", recoveryHash), ("$id", accountId));
        Run(db, "UPDATE members SET wrapped_key = $key WHERE account_id = $id", ("$key", wrappedKey), ("$id", accountId));
        Run(db, "DELETE FROM used_refresh WHERE session_id IN (SELECT id FROM sessions WHERE account_id = $id)", ("$id", accountId));
        Run(db, "DELETE FROM sessions WHERE account_id = $id", ("$id", accountId));
        transaction.Commit();
    }

    /// <summary>
    /// A new password while signed in: the sign-in key's hash and the member's locked key; every other device is
    /// signed out. The recovery key stays as it is.
    /// </summary>
    public void ChangePassword(string accountId, string salt, int rounds, byte[] keyHash, byte[] keySalt, string wrappedKey, string keepSession)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Run(db, "UPDATE accounts SET salt = $salt, rounds = $rounds, key_hash = $hash, key_salt = $keysalt, failed = 0, locked_until = NULL WHERE id = $id",
            ("$salt", salt), ("$rounds", rounds), ("$hash", keyHash), ("$keysalt", keySalt), ("$id", accountId));
        Run(db, "UPDATE members SET wrapped_key = $key WHERE account_id = $id", ("$key", wrappedKey), ("$id", accountId));
        Run(db, "DELETE FROM used_refresh WHERE session_id IN (SELECT id FROM sessions WHERE account_id = $id AND id <> $keep)", ("$id", accountId), ("$keep", keepSession));
        Run(db, "DELETE FROM sessions WHERE account_id = $id AND id <> $keep", ("$id", accountId), ("$keep", keepSession));
        transaction.Commit();
    }

    /// <summary>
    /// Takes a member out of a workspace for good: their account, devices and recovery codes go, and any invite
    /// still open is cancelled. False when there's no such member (the owner can't be removed this way).
    /// </summary>
    public bool RemoveMember(string workspaceId, string email, DateTimeOffset now)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        string? accountId;
        using (var find = Command(db, """
            SELECT a.id FROM members m JOIN accounts a ON a.id = m.account_id
            WHERE m.workspace_id = $w AND a.email = $email AND m.role <> 'owner'
            """, [("$w", workspaceId), ("$email", email)]))
            accountId = find.ExecuteScalar() as string;
        if (accountId is null)
            return false;
        Run(db, "DELETE FROM used_refresh WHERE session_id IN (SELECT id FROM sessions WHERE account_id = $a)", ("$a", accountId));
        Run(db, "DELETE FROM sessions WHERE account_id = $a", ("$a", accountId));
        Run(db, "DELETE FROM recovery_codes WHERE account_id = $a", ("$a", accountId));
        Run(db, "DELETE FROM sign_in_failures WHERE account_id = $a", ("$a", accountId));
        Run(db, "DELETE FROM members WHERE account_id = $a", ("$a", accountId));
        Run(db, "DELETE FROM accounts WHERE id = $a", ("$a", accountId));
        Run(db, "UPDATE invites SET expires = $now WHERE workspace_id = $w AND used_by IS NULL AND expires > $now", ("$now", When(now)), ("$w", workspaceId));
        transaction.Commit();
        return true;
    }

    // Signed-in devices

    /// <summary>A signed-in device; <see cref="Created"/> is when it signed in, which limits how long refreshing can keep it going.</summary>
    public sealed record Session(string Id, string AccountId, string Device, DateTimeOffset AccessExpires, DateTimeOffset RefreshExpires,
        DateTimeOffset Created = default);

    private static Session SessionRow(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetString(2), When(r.GetString(3)), When(r.GetString(4)), When(r.GetString(5)));

    public void AddSession(Session session, string accessHash, string refreshHash, DateTimeOffset now) =>
        Run("""
            INSERT INTO sessions (id, account_id, device, access_hash, access_expires, refresh_hash, refresh_expires, created)
            VALUES ($id, $account, $device, $access, $accessExpires, $refresh, $refreshExpires, $now)
            """,
            ("$id", session.Id), ("$account", session.AccountId), ("$device", session.Device), ("$access", accessHash),
            ("$accessExpires", When(session.AccessExpires)), ("$refresh", refreshHash), ("$refreshExpires", When(session.RefreshExpires)), ("$now", When(now)));

    public Session? SessionByAccess(string accessHash) =>
        Read("SELECT id, account_id, device, access_expires, refresh_expires, created FROM sessions WHERE access_hash = $h", SessionRow, ("$h", accessHash)).FirstOrDefault();

    public Session? SessionByRefresh(string refreshHash) =>
        Read("SELECT id, account_id, device, access_expires, refresh_expires, created FROM sessions WHERE refresh_hash = $h", SessionRow, ("$h", refreshHash)).FirstOrDefault();

    /// <summary>The device that used this refresh token before, however long ago: a sign that a token was taken.</summary>
    public Session? SessionByOldRefresh(string refreshHash) =>
        Read("""
            SELECT s.id, s.account_id, s.device, s.access_expires, s.refresh_expires, s.created
            FROM used_refresh u JOIN sessions s ON s.id = u.session_id WHERE u.hash = $h
            """, SessionRow, ("$h", refreshHash)).FirstOrDefault();

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
        if (command.ExecuteNonQuery() != 1)
            return false;
        Run(db, "INSERT OR IGNORE INTO used_refresh (hash, session_id) VALUES ($h, $id)", ("$h", oldRefreshHash), ("$id", id));
        return true;
    }

    public void RemoveSession(string id)
    {
        Run("DELETE FROM used_refresh WHERE session_id = $id", ("$id", id));
        Run("DELETE FROM sessions WHERE id = $id", ("$id", id));
    }

    /// <summary>Signs out every device of an account, except <paramref name="keep"/> when given.</summary>
    public void RemoveSessionsOf(string accountId, string? keep = null)
    {
        Run("DELETE FROM used_refresh WHERE session_id IN (SELECT id FROM sessions WHERE account_id = $a AND id IS NOT $keep)", ("$a", accountId), ("$keep", keep));
        Run("DELETE FROM sessions WHERE account_id = $a AND id IS NOT $keep", ("$a", accountId), ("$keep", keep));
    }
}
