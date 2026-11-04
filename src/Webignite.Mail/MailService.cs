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

/// <summary>The marks a message can carry on the server, apart from categories.</summary>
public enum MailMark
{
    Read,
    Important,
    Answered,
}

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

    // Shared by every request: the connector serves two assistant requests at once, so a plain dictionary could
    // give one mailbox two connections or lose one.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Connection> connections = new();

    public static string SecretName(MailAccount account) => "Mail/" + account.Address;

    public bool HasPassword(MailAccount account) => !string.IsNullOrEmpty(secrets.Read(SecretName(account)));

    public void SavePassword(MailAccount account, string password) => secrets.Write(SecretName(account), password);

    public async Task RemoveAccountAsync(MailAccount account)
    {
        if (connections.TryRemove(account.Id, out var connection))
            await connection.DisposeWhenFreeAsync();
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
                    var cachedNow = store.GetMailSummaries(account.Id, path, recent.Select(u => u.Id).ToHashSet());
                    var changed = new List<MailSummary>();
                    foreach (var f in flags)
                    {
                        if (!cachedNow.TryGetValue(f.UniqueId.Id, out var summary) || f.Flags is not { } marks)
                            continue;
                        var keywords = f.Keywords?.ToList() ?? [];
                        var sameKeywords = MailCategories.SameKeywords(summary.Keywords, keywords);
                        var updated = summary with
                        {
                            Seen = marks.HasFlag(MessageFlags.Seen),
                            Flagged = marks.HasFlag(MessageFlags.Flagged),
                            Answered = marks.HasFlag(MessageFlags.Answered),
                            Keywords = sameKeywords ? summary.Keywords : keywords,
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
        MarkAsync(account, [summary], MailMark.Read, seen, cancel);

    public Task SetFlaggedAsync(MailAccount account, MailSummary summary, bool flagged, CancellationToken cancel = default) =>
        MarkAsync(account, [summary], MailMark.Important, flagged, cancel);

    /// <summary>Marks mail from one mailbox read or unread, important or not, all in one go. Returns the mail as it is now.</summary>
    public async Task<IReadOnlyList<MailSummary>> MarkAsync(MailAccount account, IReadOnlyList<MailSummary> mail, MailMark mark, bool on,
        CancellationToken cancel = default)
    {
        var flag = mark switch
        {
            MailMark.Read => MessageFlags.Seen,
            MailMark.Important => MessageFlags.Flagged,
            _ => MessageFlags.Answered,
        };
        await StoreAsync(account, mail, new StoreFlagsRequest(on ? StoreAction.Add : StoreAction.Remove, flag) { Silent = true }, cancel);
        var updated = mail.Select(s => mark switch
        {
            MailMark.Read => s with { Seen = on },
            MailMark.Important => s with { Flagged = on },
            _ => s with { Answered = on },
        }).ToList();
        store.SaveMailSummaries(updated);
        return updated;
    }

    /// <summary>Puts mail from one mailbox in a category, or takes it out, by adding or removing the keyword on the server.</summary>
    public async Task<IReadOnlyList<MailSummary>> SetCategoryAsync(MailAccount account, IReadOnlyList<MailSummary> mail, string keyword, bool on,
        CancellationToken cancel = default)
    {
        await StoreAsync(account, mail, new StoreFlagsRequest(on ? StoreAction.Add : StoreAction.Remove, [keyword]) { Silent = true }, cancel);
        var updated = mail.Select(s => MailCategories.With(s, keyword, on)).ToList();
        store.SaveMailSummaries(updated);
        return updated;
    }

    private Task StoreAsync(MailAccount account, IReadOnlyList<MailSummary> mail, StoreFlagsRequest request, CancellationToken cancel) =>
        mail.Count == 0 ? Task.CompletedTask : WithImap(account, async imap =>
        {
            foreach (var group in mail.GroupBy(m => m.Folder))
            {
                var folder = await imap.GetFolderAsync(group.Key, cancel);
                await folder.OpenAsync(FolderAccess.ReadWrite, cancel);
                try
                {
                    await folder.StoreAsync(Uids(group), request, cancel);
                }
                finally
                {
                    await folder.CloseAsync(false, cancel);
                }
            }
            return true;
        }, cancel);

    /// <summary>Moves a message to another folder. The copy on this PC follows when that folder next syncs.</summary>
    public Task MoveAsync(MailAccount account, MailSummary summary, string target, CancellationToken cancel = default) =>
        MoveAsync(account, [summary], target, cancel);

    /// <summary>Moves mail from one mailbox to another of its folders.</summary>
    public async Task MoveAsync(MailAccount account, IReadOnlyList<MailSummary> mail, string target, CancellationToken cancel = default)
    {
        var moving = mail.Where(m => m.Folder != target).ToList();
        if (moving.Count == 0)
            return;
        await WithImap(account, async imap =>
        {
            var destination = await imap.GetFolderAsync(target, cancel);
            foreach (var group in moving.GroupBy(m => m.Folder))
            {
                var folder = await imap.GetFolderAsync(group.Key, cancel);
                await folder.OpenAsync(FolderAccess.ReadWrite, cancel);
                try
                {
                    await folder.MoveToAsync(Uids(group), destination, cancel);
                }
                finally
                {
                    await folder.CloseAsync(true, cancel);
                }
            }
            return true;
        }, cancel);
        foreach (var group in moving.GroupBy(m => m.Folder))
            store.DeleteMailMessages(account.Id, group.Key, group.Select(m => m.Uid).ToList());
    }

    /// <summary>
    /// Files mail in the mailbox's Archive folder, making one under Inbox if the mailbox has none yet.
    /// Returns the folder's name.
    /// </summary>
    public async Task<string> ArchiveAsync(MailAccount account, IReadOnlyList<MailSummary> mail, CancellationToken cancel = default)
    {
        var archive = FolderNames.Archive(store.ListMailFolders(account.Id)) ?? await CreateFolderAsync(account, null, "Archive", cancel);
        await MoveAsync(account, mail.Where(m => m.Folder != archive.Path).ToList(), archive.Path, cancel);
        return archive.Name;
    }

    /// <summary>Deleting moves to Trash. Deleting from Trash removes it for good.</summary>
    public Task DeleteAsync(MailAccount account, MailSummary summary, CancellationToken cancel = default) =>
        DeleteAsync(account, [summary], cancel);

    /// <summary>
    /// Mail from one mailbox goes to its Trash, making a Trash folder first if the mailbox has none Webignite
    /// knows of. Only mail already in Trash is removed for good, so nothing outside Trash is ever expunged.
    /// </summary>
    public async Task DeleteAsync(MailAccount account, IReadOnlyList<MailSummary> mail, CancellationToken cancel = default)
    {
        var trash = store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == FolderRole.Trash)
            ?? await CreateFolderAsync(account, null, "Trash", cancel);
        if (trash.Role != FolderRole.Trash)
            throw new MailException("This mailbox has no Trash folder Webignite can use, so nothing was deleted.");
        var toTrash = mail.Where(m => m.Folder != trash.Path).ToList();
        if (toTrash.Count > 0)
            await MoveAsync(account, toTrash, trash.Path, cancel);
        var forGood = mail.Except(toTrash).ToList();
        if (forGood.Count == 0)
            return;
        await WithImap(account, async imap =>
        {
            foreach (var group in forGood.GroupBy(m => m.Folder))
            {
                var folder = await imap.GetFolderAsync(group.Key, cancel);
                await folder.OpenAsync(FolderAccess.ReadWrite, cancel);
                try
                {
                    await Expunge(imap, folder, Uids(group), cancel);
                }
                finally
                {
                    await folder.CloseAsync(false, cancel);
                }
            }
            return true;
        }, cancel);
        foreach (var group in forGood.GroupBy(m => m.Folder))
            store.DeleteMailMessages(account.Id, group.Key, group.Select(m => m.Uid).ToList());
    }

    /// <summary>
    /// Makes a folder of your own, inside another folder or, when none is given, inside Inbox. It is subscribed
    /// so it shows in Thunderbird and on your phone too.
    /// </summary>
    public async Task<MailFolder> CreateFolderAsync(MailAccount account, string? parentPath, string name, CancellationToken cancel = default)
    {
        var made = await WithImap(account, async imap =>
        {
            var parent = parentPath is null ? imap.Inbox : await imap.GetFolderAsync(parentPath, cancel);
            var folder = await parent.CreateAsync(name.Trim(), true, cancel)
                ?? throw new MailException("The mail server didn't make the folder.");
            await Subscribe(folder, cancel);
            var role = MailFolders.Guess(folder.FullName, folder.DirectorySeparator);
            return new MailFolder
            {
                AccountId = account.Id,
                Path = folder.FullName,
                Name = MailFolders.DisplayName(folder.FullName, folder.DirectorySeparator, role),
                Role = role,
            };
        }, cancel, repeatable: false);
        store.SaveMailFolder(made);
        return made;
    }

    /// <summary>Renames one of your own folders on the server, then reads the folder list again.</summary>
    public async Task RenameFolderAsync(MailAccount account, string path, string name, CancellationToken cancel = default)
    {
        await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(path, cancel);
            var parent = folder.ParentFolder ?? throw new MailException("This folder can't be renamed.");
            await folder.RenameAsync(parent, name.Trim(), cancel);
            await Subscribe(folder, cancel);
            return true;
        }, cancel);
        await SyncAccountAsync(account, cancel);
    }

    /// <summary>Removes one of your own folders from the server. Only an empty folder with nothing inside it can go.</summary>
    public async Task DeleteFolderAsync(MailAccount account, string path, CancellationToken cancel = default)
    {
        await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(path, cancel);
            if ((await folder.GetSubfoldersAsync(false, cancel)).Count > 0)
                throw new MailException("This folder has folders inside it. Remove those first.");
            await folder.OpenAsync(FolderAccess.ReadOnly, cancel);
            var count = folder.Count;
            await folder.CloseAsync(false, cancel);
            if (count > 0)
                throw new MailException("There's still mail in this folder. Move or delete it first.");
            try
            {
                await folder.UnsubscribeAsync(cancel);
            }
            catch (ImapCommandException)
            {
                // Not every server keeps subscriptions; the folder can still go.
            }
            await folder.DeleteAsync(cancel);
            return true;
        }, cancel);
        store.KeepOnlyMailFolders(account.Id, store.ListMailFolders(account.Id).Where(f => f.Path != path).Select(f => f.Path).ToList());
    }

    private static async Task Subscribe(IMailFolder folder, CancellationToken cancel)
    {
        try
        {
            await folder.SubscribeAsync(cancel);
        }
        catch (ImapCommandException)
        {
            // Servers that don't keep subscriptions show every folder anyway.
        }
    }

    private static List<UniqueId> Uids(IEnumerable<MailSummary> mail) => mail.Select(m => new UniqueId(m.Uid)).ToList();

    /// <summary>
    /// Sends a finished mail, then files a copy in Sent, marks the original as answered and removes the
    /// draft. Sending is never retried: if the connection drops part way, the mail may already have gone.
    /// Returns a note when the mail went but the tidying up afterwards didn't.
    /// </summary>
    public async Task<string?> SendAsync(MailAccount account, MimeMessage message, MailSummary? answering = null, uint? draftUid = null,
        CancellationToken cancel = default)
    {
        var password = secrets.Read(SecretName(account))
            ?? throw new MailException($"The password for {account.Address} isn't saved on this PC yet. Add it under Manage mailboxes.");
        var handedOver = false;
        try
        {
            using var smtp = new SmtpClient { Timeout = 120_000 };
            await smtp.ConnectAsync(account.SmtpHost, account.SmtpPort, SecureSocketOptions.Auto, cancel);
            await smtp.AuthenticateAsync(account.SignInName, password, cancel);
            handedOver = true;
            await smtp.SendAsync(message, cancel);
            await smtp.DisconnectAsync(true, CancellationToken.None);
        }
        catch (SmtpCommandException e) when (e.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
        {
            throw new MailException($"The mail server refused {e.Mailbox?.Address ?? "one of the addresses"}. Nothing was sent.", e);
        }
        catch (Exception e) when (e is not OperationCanceledException && handedOver && e is not SmtpCommandException)
        {
            throw new MailException("The connection dropped while sending, so it may or may not have gone. Check with the person or look in Sent before sending again.", e);
        }
        catch (Exception e) when (e is not OperationCanceledException and not MailException)
        {
            throw Explain(e);
        }

        var notes = new List<string>();
        try
        {
            await AppendAsync(account, FolderRole.Sent, message, MessageFlags.Seen, cancel);
        }
        catch (MailException e)
        {
            notes.Add("Sent, but a copy couldn't be saved in Sent: " + e.Message);
        }
        if (answering is not null)
        {
            try { await MarkAsync(account, [answering], MailMark.Answered, true, cancel); }
            catch (MailException) { /* The answered mark is a nicety; the mail has gone. */ }
        }
        if (draftUid is { } uid)
        {
            try { await DeleteDraftAsync(account, uid, cancel); }
            catch (MailException) { notes.Add("Sent, but the draft is still in Drafts."); }
        }
        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    /// <summary>Saves a draft in the mailbox's Drafts folder on the server, replacing the earlier copy.</summary>
    public async Task<uint?> SaveDraftAsync(MailAccount account, MimeMessage message, uint? replacing = null, CancellationToken cancel = default)
    {
        var uid = await AppendAsync(account, FolderRole.Drafts, message, MessageFlags.Seen | MessageFlags.Draft, cancel);
        if (replacing is { } old && old != uid)
            await DeleteDraftAsync(account, old, cancel);
        if (Folder(account, FolderRole.Drafts) is { } drafts)
        {
            if (uid is { } saved)
                store.SaveMailBody(account.Id, drafts.Path, saved, MailComposer.ToBytes(message));
            await SyncFolderAsync(account, drafts.Path, cancel: cancel);
        }
        return uid;
    }

    public async Task DeleteDraftAsync(MailAccount account, uint uid, CancellationToken cancel = default)
    {
        if (Folder(account, FolderRole.Drafts) is not { } drafts)
            return;
        await WithImap(account, async imap =>
        {
            var folder = await imap.GetFolderAsync(drafts.Path, cancel);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancel);
            try
            {
                await Expunge(imap, folder, [new UniqueId(uid)], cancel);
            }
            finally
            {
                await folder.CloseAsync(false, cancel);
            }
            return true;
        }, cancel);
        store.DeleteMailMessages(account.Id, drafts.Path, [uid]);
    }

    private MailFolder? Folder(MailAccount account, FolderRole role) =>
        store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == role);

    private async Task<uint?> AppendAsync(MailAccount account, FolderRole role, MimeMessage message, MessageFlags flags, CancellationToken cancel)
    {
        var known = Folder(account, role);
        return await WithImap(account, async imap =>
        {
            IMailFolder? folder = known is not null ? await imap.GetFolderAsync(known.Path, cancel)
                : imap.Capabilities.HasFlag(ImapCapabilities.SpecialUse) ? imap.GetFolder(role == FolderRole.Sent ? SpecialFolder.Sent : SpecialFolder.Drafts)
                : null;
            if (folder is null)
                throw new MailException($"This mailbox has no {(role == FolderRole.Sent ? "Sent" : "Drafts")} folder yet. Press Check for mail, then try again.");
            var uid = await folder.AppendAsync(new AppendRequest(message, flags), cancel);
            return uid?.Id;
        }, cancel, repeatable: false);
    }

    private static async Task Expunge(ImapClient imap, IMailFolder folder, IList<UniqueId> uids, CancellationToken cancel)
    {
        await folder.StoreAsync(uids, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) { Silent = true }, cancel);
        if (imap.Capabilities.HasFlag(ImapCapabilities.UidPlus))
            await folder.ExpungeAsync(uids, cancel);
        else
            await folder.ExpungeAsync(cancel);
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
            Keywords = s.Keywords?.ToList() ?? [],
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

    /// <summary>
    /// Runs one job on the mailbox's connection, connecting or reconnecting first if needed. A connection that
    /// drops is tried once more, but a job that adds something to the server (<paramref name="repeatable"/> false,
    /// such as saving a copy to Sent or Drafts) is only tried again when it never started, so a retry can't
    /// leave two copies.
    /// </summary>
    private async Task<T> WithImap<T>(MailAccount account, Func<ImapClient, Task<T>> job, CancellationToken cancel, bool repeatable = true)
    {
        var connection = connections.GetOrAdd(account.Id, _ => new Connection());
        await connection.Lock.WaitAsync(cancel);
        try
        {
            var password = secrets.Read(SecretName(account))
                ?? throw new MailException($"The password for {account.Address} isn't saved on this PC yet. Add it under Mail settings.");
            for (var attempt = 0; ; attempt++)
            {
                var started = false;
                try
                {
                    if (!connection.Client.IsConnected)
                        await connection.Client.ConnectAsync(account.ImapHost, account.ImapPort, SecureSocketOptions.Auto, cancel);
                    if (!connection.Client.IsAuthenticated)
                        await connection.Client.AuthenticateAsync(account.SignInName, password, cancel);
                    started = true;
                    return await job(connection.Client);
                }
                catch (Exception e) when (MailRetry.TryAgain(attempt, started, repeatable, e))
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
        foreach (var id in connections.Keys)
            if (connections.TryRemove(id, out var connection))
                await connection.DisposeWhenFreeAsync();
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

        /// <summary>
        /// Waits for a job still running on this connection (up to a few seconds) before closing it, so a sync in
        /// progress isn't cut off with a confusing error.
        /// </summary>
        public async Task DisposeWhenFreeAsync()
        {
            var free = await Lock.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await DisposeAsync();
            }
            finally
            {
                if (free)
                    Lock.Release();
            }
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
