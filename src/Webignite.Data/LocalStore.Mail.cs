using Webignite.Core.Mail;

namespace Webignite.Data;

/// <summary>
/// The mail kept on this PC so it reads offline and searches fast. Folder names and message numbers
/// are readable in the file; senders, subjects and bodies are encrypted like everything else.
/// </summary>
public sealed partial class LocalStore
{
    public MailAccount SaveMailAccount(MailAccount account)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO mail_accounts (id, payload) VALUES ($id, $payload) ON CONFLICT (id) DO UPDATE SET payload = $payload";
        command.Parameters.AddWithValue("$id", account.Id);
        command.Parameters.AddWithValue("$payload", Seal("mail-account:" + account.Id, account));
        command.ExecuteNonQuery();
        return account;
    }

    public IReadOnlyList<MailAccount> ListMailAccounts()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM mail_accounts";
        using var reader = command.ExecuteReader();
        var list = new List<MailAccount>();
        while (reader.Read())
            list.Add(Open<MailAccount>("mail-account:" + reader.GetString(0), (byte[])reader[1]));
        return list.OrderBy(a => a.Created).ThenBy(a => a.Address, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Removes a mailbox and all mail cached for it from this PC. Nothing changes on the server.</summary>
    public void DeleteMailAccount(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM mail_bodies WHERE account_id = $id;
            DELETE FROM mail_messages WHERE account_id = $id;
            DELETE FROM mail_folders WHERE account_id = $id;
            DELETE FROM mail_accounts WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SaveMailFolder(MailFolder folder)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mail_folders (account_id, path, payload) VALUES ($account, $path, $payload)
            ON CONFLICT (account_id, path) DO UPDATE SET payload = $payload
            """;
        command.Parameters.AddWithValue("$account", folder.AccountId);
        command.Parameters.AddWithValue("$path", folder.Path);
        command.Parameters.AddWithValue("$payload", Seal(FolderContext(folder.AccountId, folder.Path), folder));
        command.ExecuteNonQuery();
    }

    /// <summary>Forgets folders the server no longer has, with their cached mail.</summary>
    public void KeepOnlyMailFolders(string accountId, IReadOnlyCollection<string> paths)
    {
        foreach (var folder in ListMailFolders(accountId).Where(f => !paths.Contains(f.Path)))
        {
            ClearMailFolder(accountId, folder.Path);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM mail_folders WHERE account_id = $account AND path = $path";
            command.Parameters.AddWithValue("$account", accountId);
            command.Parameters.AddWithValue("$path", folder.Path);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<MailFolder> ListMailFolders(string accountId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, payload FROM mail_folders WHERE account_id = $account";
        command.Parameters.AddWithValue("$account", accountId);
        using var reader = command.ExecuteReader();
        var list = new List<MailFolder>();
        while (reader.Read())
            list.Add(Open<MailFolder>(FolderContext(accountId, reader.GetString(0)), (byte[])reader[1]));
        return MailFolders.Sort(list);
    }

    public void SaveMailSummaries(IEnumerable<MailSummary> summaries)
    {
        Execute("BEGIN");
        try
        {
            foreach (var summary in summaries)
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO mail_messages (account_id, folder, uid, date, payload) VALUES ($account, $folder, $uid, $date, $payload)
                    ON CONFLICT (account_id, folder, uid) DO UPDATE SET date = $date, payload = $payload
                    """;
                command.Parameters.AddWithValue("$account", summary.AccountId);
                command.Parameters.AddWithValue("$folder", summary.Folder);
                command.Parameters.AddWithValue("$uid", (long)summary.Uid);
                command.Parameters.AddWithValue("$date", summary.Date.UtcDateTime.ToString("O"));
                command.Parameters.AddWithValue("$payload", Seal(MessageContext(summary.AccountId, summary.Folder, summary.Uid), summary));
                command.ExecuteNonQuery();
            }
            Execute("COMMIT");
        }
        catch
        {
            Execute("ROLLBACK");
            throw;
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<MailSummary> ListMailSummaries(string accountId, string folder, int limit = 500)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT uid, payload FROM mail_messages WHERE account_id = $account AND folder = $folder
            ORDER BY date DESC, uid DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$limit", limit);
        return ReadSummaries(command, accountId, folder);
    }

    public MailSummary? GetMailSummary(string accountId, string folder, uint uid)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT uid, payload FROM mail_messages WHERE account_id = $account AND folder = $folder AND uid = $uid";
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$uid", (long)uid);
        return ReadSummaries(command, accountId, folder).FirstOrDefault();
    }

    /// <summary>Every message number cached for a folder, so a sync only fetches what is new.</summary>
    public IReadOnlySet<uint> KnownMailUids(string accountId, string folder)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT uid FROM mail_messages WHERE account_id = $account AND folder = $folder";
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$folder", folder);
        using var reader = command.ExecuteReader();
        var uids = new HashSet<uint>();
        while (reader.Read())
            uids.Add((uint)reader.GetInt64(0));
        return uids;
    }

    public void DeleteMailMessages(string accountId, string folder, IEnumerable<uint> uids)
    {
        foreach (var uid in uids)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM mail_messages WHERE account_id = $account AND folder = $folder AND uid = $uid;
                DELETE FROM mail_bodies WHERE account_id = $account AND folder = $folder AND uid = $uid;
                """;
            command.Parameters.AddWithValue("$account", accountId);
            command.Parameters.AddWithValue("$folder", folder);
            command.Parameters.AddWithValue("$uid", (long)uid);
            command.ExecuteNonQuery();
        }
    }

    public void ClearMailFolder(string accountId, string folder)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM mail_messages WHERE account_id = $account AND folder = $folder;
            DELETE FROM mail_bodies WHERE account_id = $account AND folder = $folder;
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$folder", folder);
        command.ExecuteNonQuery();
    }

    /// <summary>Keeps the whole message as it came from the server, encrypted.</summary>
    public void SaveMailBody(string accountId, string folder, uint uid, byte[] mime)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mail_bodies (account_id, folder, uid, payload) VALUES ($account, $folder, $uid, $payload)
            ON CONFLICT (account_id, folder, uid) DO UPDATE SET payload = $payload
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$uid", (long)uid);
        command.Parameters.AddWithValue("$payload", vault.Encrypt(mime, "mail-body:" + MessageContext(accountId, folder, uid)));
        command.ExecuteNonQuery();
    }

    public byte[]? GetMailBody(string accountId, string folder, uint uid)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM mail_bodies WHERE account_id = $account AND folder = $folder AND uid = $uid";
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$uid", (long)uid);
        return command.ExecuteScalar() is byte[] payload ? vault.Decrypt(payload, "mail-body:" + MessageContext(accountId, folder, uid)) : null;
    }

    /// <summary>
    /// Searches cached mail by sender, recipients, subject and preview. Every word has to match.
    /// Newest first.
    /// </summary>
    public IReadOnlyList<MailSummary> SearchMail(IEnumerable<string> accountIds, string query, int limit = 200)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<MailSummary>();
        foreach (var accountId in accountIds)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT folder, uid, payload FROM mail_messages WHERE account_id = $account ORDER BY date DESC";
            command.Parameters.AddWithValue("$account", accountId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var summary = Open<MailSummary>(MessageContext(accountId, reader.GetString(0), (uint)reader.GetInt64(1)), (byte[])reader[2]);
                if (words.All(w => MailSearch.Matches(summary, w)))
                    found.Add(summary);
            }
        }
        return found.OrderByDescending(s => s.Date).Take(limit).ToList();
    }

    private List<MailSummary> ReadSummaries(Microsoft.Data.Sqlite.SqliteCommand command, string accountId, string folder)
    {
        using var reader = command.ExecuteReader();
        var list = new List<MailSummary>();
        while (reader.Read())
            list.Add(Open<MailSummary>(MessageContext(accountId, folder, (uint)reader.GetInt64(0)), (byte[])reader[1]));
        return list;
    }

    private static string FolderContext(string accountId, string path) => $"mail-folder:{accountId}:{path}";
    private static string MessageContext(string accountId, string folder, uint uid) => $"mail:{accountId}:{folder}:{uid}";
}
