using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Webignite.Core.Mail;
using MailFolder = Webignite.Core.Mail.MailFolder;
using Webignite.Data;

namespace Webignite.Mail;

/// <summary>A mail problem worded for a person, e.g. a wrong password or no internet.</summary>
public sealed class MailException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Talks to the mail servers and keeps the copy on this PC up to date. One connection per mailbox,
/// used one job at a time. Reading works from the copy, so mail opens without internet.
/// </summary>
public sealed class MailService(LocalStore store, ISecretStore secrets) : IAsyncDisposable
{
    /// <summary>How many of the newest messages a folder fetches the first time. Older ones come with "Load older".</summary>
    public const int FirstSync = 300;

    private readonly Dictionary<string, Connection> connections = [];

    public static string SecretName(MailAccount account) => "Mail/" + account.Address;

    public bool HasPassword(MailAccount account) => !string.IsNullOrEmpty(secrets.Read(SecretName(account)));

    public void SavePassword(MailAccount account, string password) => secrets.Write(SecretName(account), password);

    public async Task RemoveAccountAsync(MailAccount account)
    {
        if (connections.Remove(account.Id, out var connection))
            await connection.DisposeAsync();
        secrets.Delete(SecretName(account));
        store.DeleteMailAccount(account.Id);
    }

    /// <summary>Signs in to both servers with a password before it is saved. Returns null when all is well.</summary>
    public static async Task<string?> TestAsync(MailAccount account, string password, CancellationToken cancel = default)
    {
        try
        {
            using (var imap = new ImapClient { Timeout = 30_000 })
            {
                await imap.ConnectAsync(account.ImapHost, account.ImapPort, SecureSocketOptions.Auto, cancel);
                await imap.AuthenticateAsync(account.SignInName, password, cancel);
                await imap.DisconnectAsync(true, cancel);
            }
            using (var smtp = new SmtpClient { Timeout = 30_000 })
            {
                await smtp.ConnectAsync(account.SmtpHost, account.SmtpPort, SecureSocketOptions.Auto, cancel);
                await smtp.AuthenticateAsync(account.SignInName, password, cancel);
                await smtp.DisconnectAsync(true, cancel);
            }
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Explain(e).Message;
        }
    }

    /// <summary>Fetches the folder list and refreshes the folders that matter most.</summary>
    public async Task SyncAccountAsync(MailAccount account, CancellationToken cancel = default)
    {
        var folders = await WithImap(account, async imap =>
        {
            var personal = imap.GetFolder(imap.PersonalNamespaces[0]);
            var all = new List<IMailFolder> { imap.Inbox };
            all.AddRange((await personal.GetSubfoldersAsync(true, cancel)).Where(f => !f.Attributes.HasFlag(FolderAttributes.NonExistent) && !f.Attributes.HasFlag(FolderAttributes.NoSelect)));
            return all.DistinctBy(f => f.FullName).ToList();
        }, cancel);

        var known = store.ListMailFolders(account.Id).ToDictionary(f => f.Path);
        foreach (var folder in folders)
        {
            var role = Role(folder);
            store.SaveMailFolder((known.GetValueOrDefault(folder.FullName) ?? new MailFolder { AccountId = account.Id, Path = folder.FullName, Name = "" })
                with { Name = MailFolders.DisplayName(folder.FullName, folder.DirectorySeparator, role), Role = role });
        }
        store.KeepOnlyMailFolders(account.Id, folders.Select(f => f.FullName).ToList());

        foreach (var folder in store.ListMailFolders(account.Id).Where(f => f.Role is FolderRole.Inbox or FolderRole.Sent or FolderRole.Drafts))
            await SyncFolderAsync(account, folder.Path, cancel: cancel);
    }

    /// <summary>
    /// Brings one folder up to date: new mail is fetched, mail removed on the server is removed here,
    /// and read and flag marks are copied across.
    /// </summary>
    public async Task SyncFolderAsync(MailAccount account, string path, bool older = false, CancellationToken cancel = default)
    {
        var cached = store.ListMailFolders(account.Id).FirstOrDefault(f => f.Path == path)
            ?? new MailFolder { AccountId = account.Id, Path = path, Name = path };

        await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(path, cancel);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancel);
            try
            {
                if (cached.UidValidity != 0 && cached.UidValidity != folder.UidValidity)
                    store.ClearMailFolder(account.Id, path);

                var onServer = await folder.SearchAsync(SearchQuery.All, cancel);
                var serverUids = onServer.Select(u => u.Id).ToHashSet();
                var known = store.KnownMailUids(account.Id, path);

                store.DeleteMailMessages(account.Id, path, known.Where(u => !serverUids.Contains(u)).ToList());

                var missing = onServer.Where(u => !known.Contains(u.Id)).OrderByDescending(u => u.Id).ToList();
                var oldestKnown = known.Count == 0 ? uint.MaxValue : known.Min();
                var toFetch = known.Count == 0
                    ? missing.Take(FirstSync).ToList()
                    : missing.Where(u => u.Id > oldestKnown).Concat(older ? missing.Where(u => u.Id < oldestKnown).Take(FirstSync) : []).ToList();

                const MessageSummaryItems items = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags
                    | MessageSummaryItems.InternalDate | MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText;
                foreach (var batch in toFetch.Chunk(100))
                {
                    var summaries = await folder.FetchAsync(batch, items, cancel);
                    store.SaveMailSummaries(summaries.Select(s => ToSummary(account.Id, path, s)));
                }

                // Read and flag marks can change from your phone, so the newest ones are checked each time.
                var recent = store.KnownMailUids(account.Id, path).OrderByDescending(u => u).Take(500).Select(u => new UniqueId(u)).ToList();
                if (recent.Count > 0)
                {
                    var flags = await folder.FetchAsync(recent, MessageSummaryItems.UniqueId | MessageSummaryItems.Flags, cancel);
                    var changed = new List<MailSummary>();
                    foreach (var f in flags)
                    {
                        if (store.GetMailSummary(account.Id, path, f.UniqueId.Id) is not { } summary || f.Flags is not { } marks)
                            continue;
                        var updated = summary with
                        {
                            Seen = marks.HasFlag(MessageFlags.Seen),
                            Flagged = marks.HasFlag(MessageFlags.Flagged),
                            Answered = marks.HasFlag(MessageFlags.Answered),
                        };
                        if (updated != summary)
                            changed.Add(updated);
                    }
                    store.SaveMailSummaries(changed);
                }

                var unread = await folder.SearchAsync(SearchQuery.NotSeen, cancel);
                store.SaveMailFolder(cached with
                {
                    UidValidity = folder.UidValidity,
                    Total = folder.Count,
                    Unread = unread.Count,
                    Synced = DateTimeOffset.UtcNow,
                });
            }
            finally
            {
                await folder.CloseAsync(false, cancel);
            }
            return true;
        }, cancel);
    }

    /// <summary>The whole message, from this PC if it was opened before, otherwise from the server.</summary>
    public async Task<byte[]> GetMessageAsync(MailAccount account, string path, uint uid, CancellationToken cancel = default)
    {
        if (store.GetMailBody(account.Id, path, uid) is { } cached)
            return cached;
        var mime = await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(path, cancel);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancel);
            try
            {
                var message = await folder.GetMessageAsync(new UniqueId(uid), cancel);
                using var buffer = new MemoryStream();
                await message.WriteToAsync(buffer, cancel);
                return buffer.ToArray();
            }
            finally
            {
                await folder.CloseAsync(false, cancel);
            }
        }, cancel);
        store.SaveMailBody(account.Id, path, uid, mime);
        return mime;
    }

    public Task SetSeenAsync(MailAccount account, MailSummary summary, bool seen, CancellationToken cancel = default) =>
        SetFlagAsync(account, summary, MessageFlags.Seen, seen, s => s with { Seen = seen }, cancel);

    public Task SetFlaggedAsync(MailAccount account, MailSummary summary, bool flagged, CancellationToken cancel = default) =>
        SetFlagAsync(account, summary, MessageFlags.Flagged, flagged, s => s with { Flagged = flagged }, cancel);

    private async Task SetFlagAsync(MailAccount account, MailSummary summary, MessageFlags flag, bool on,
        Func<MailSummary, MailSummary> local, CancellationToken cancel)
    {
        await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(summary.Folder, cancel);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancel);
            try
            {
                var request = new StoreFlagsRequest(on ? StoreAction.Add : StoreAction.Remove, flag) { Silent = true };
                await folder.StoreAsync(new UniqueId(summary.Uid), request, cancel);
            }
            finally
            {
                await folder.CloseAsync(false, cancel);
            }
            return true;
        }, cancel);
        store.SaveMailSummaries([local(summary)]);
    }

    /// <summary>Moves a message to another folder. The copy on this PC follows when that folder next syncs.</summary>
    public async Task MoveAsync(MailAccount account, MailSummary summary, string target, CancellationToken cancel = default)
    {
        if (target == summary.Folder)
            return;
        await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(summary.Folder, cancel);
            var destination = await imap.GetFolderAsync(target, cancel);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancel);
            try
            {
                await folder.MoveToAsync(new UniqueId(summary.Uid), destination, cancel);
            }
            finally
            {
                await folder.CloseAsync(true, cancel);
            }
            return true;
        }, cancel);
        store.DeleteMailMessages(account.Id, summary.Folder, [summary.Uid]);
    }

    /// <summary>Deleting moves to Trash. Deleting from Trash removes it for good.</summary>
    public async Task DeleteAsync(MailAccount account, MailSummary summary, CancellationToken cancel = default)
    {
        var trash = store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == FolderRole.Trash);
        if (trash is not null && trash.Path != summary.Folder)
        {
            await MoveAsync(account, summary, trash.Path, cancel);
            return;
        }
        await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(summary.Folder, cancel);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancel);
            try
            {
                var uid = new UniqueId(summary.Uid);
                await folder.AddFlagsAsync(uid, MessageFlags.Deleted, true, cancel);
                if (imap.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                    await folder.ExpungeAsync([uid], cancel);
                else
                    await folder.ExpungeAsync(cancel);
            }
            finally
            {
                await folder.CloseAsync(false, cancel);
            }
            return true;
        }, cancel);
        store.DeleteMailMessages(account.Id, summary.Folder, [summary.Uid]);
    }

    internal static MailSummary ToSummary(string accountId, string folder, IMessageSummary s)
    {
        var envelope = s.Envelope;
        var flags = s.Flags ?? MessageFlags.None;
        return new MailSummary
        {
            AccountId = accountId,
            Folder = folder,
            Uid = s.UniqueId.Id,
            MessageId = envelope?.MessageId,
            InReplyTo = envelope?.InReplyTo,
            Date = envelope?.Date ?? s.InternalDate ?? DateTimeOffset.MinValue,
            From = People(envelope?.From).FirstOrDefault(),
            To = People(envelope?.To),
            Cc = People(envelope?.Cc),
            Subject = envelope?.Subject ?? "",
            Preview = MailText.Preview(s.PreviewText ?? ""),
            Seen = flags.HasFlag(MessageFlags.Seen),
            Flagged = flags.HasFlag(MessageFlags.Flagged),
            Answered = flags.HasFlag(MessageFlags.Answered),
            HasAttachments = s.Attachments?.Any() == true,
            Size = s.Size ?? 0,
        };
    }

    internal static IReadOnlyList<MailPerson> People(InternetAddressList? list) =>
        list?.Mailboxes.Select(m => new MailPerson(string.IsNullOrWhiteSpace(m.Name) ? null : m.Name, m.Address)).ToList() ?? [];

    private static FolderRole Role(IMailFolder folder) =>
        folder.Attributes.HasFlag(FolderAttributes.Inbox) || folder.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? FolderRole.Inbox
        : folder.Attributes.HasFlag(FolderAttributes.Drafts) ? FolderRole.Drafts
        : folder.Attributes.HasFlag(FolderAttributes.Sent) ? FolderRole.Sent
        : folder.Attributes.HasFlag(FolderAttributes.Archive) ? FolderRole.Archive
        : folder.Attributes.HasFlag(FolderAttributes.Junk) ? FolderRole.Junk
        : folder.Attributes.HasFlag(FolderAttributes.Trash) ? FolderRole.Trash
        : MailFolders.Guess(folder.FullName, folder.DirectorySeparator);

    /// <summary>Runs one job on the mailbox's connection, connecting or reconnecting first if needed.</summary>
    private async Task<T> WithImap<T>(MailAccount account, Func<ImapClient, Task<T>> job, CancellationToken cancel)
    {
        if (!connections.TryGetValue(account.Id, out var connection))
            connections[account.Id] = connection = new Connection();
        await connection.Lock.WaitAsync(cancel);
        try
        {
            var password = secrets.Read(SecretName(account))
                ?? throw new MailException($"The password for {account.Address} isn't saved on this PC yet. Add it under Mail settings.");
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (!connection.Client.IsConnected)
                        await connection.Client.ConnectAsync(account.ImapHost, account.ImapPort, SecureSocketOptions.Auto, cancel);
                    if (!connection.Client.IsAuthenticated)
                        await connection.Client.AuthenticateAsync(account.SignInName, password, cancel);
                    return await job(connection.Client);
                }
                catch (Exception e) when (attempt == 0 && e is IOException or SocketException or ServiceNotConnectedException or ImapProtocolException)
                {
                    // A dropped connection is common after the PC sleeps. Start a fresh one and try once more.
                    connection.Reset();
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException and not MailException)
        {
            throw Explain(e);
        }
        finally
        {
            connection.Lock.Release();
        }
    }

    private static MailException Explain(Exception e) => e switch
    {
        AuthenticationException => new MailException("The mail server didn't accept the password.", e),
        SocketException or IOException when e.Message.Contains("host", StringComparison.OrdinalIgnoreCase)
            => new MailException("Couldn't reach the mail server. Check the internet connection and the server name.", e),
        SocketException or IOException or ServiceNotConnectedException => new MailException("The connection to the mail server dropped. Try again in a moment.", e),
        SslHandshakeException => new MailException("The mail server's security certificate couldn't be checked.", e),
        FolderNotFoundException => new MailException("That folder is no longer on the server.", e),
        MessageNotFoundException => new MailException("That message is no longer on the server.", e),
        _ => new MailException("The mail server said: " + e.Message, e),
    };

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in connections.Values)
            await connection.DisposeAsync();
        connections.Clear();
    }

    private sealed class Connection : IAsyncDisposable
    {
        public ImapClient Client { get; private set; } = new() { Timeout = 60_000 };
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public void Reset()
        {
            Client.Dispose();
            Client = new ImapClient { Timeout = 60_000 };
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Client.IsConnected)
                    await Client.DisconnectAsync(true);
            }
            catch (Exception)
            {
                // Closing down: nothing useful to do if the server has already gone.
            }
            Client.Dispose();
        }
    }
}
