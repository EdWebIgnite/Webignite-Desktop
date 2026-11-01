using Webignite.Core.Mail;
using Webignite.Data;

namespace Webignite.Mail;

/// <summary>
/// What Claude and ChatGPT can do with mail, through the connector: list mailboxes, search, read, and
/// save drafts. There is deliberately no way to send, delete or move mail here.
/// </summary>
public sealed class AssistantTools(LocalStore store, MailService mail, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    /// <summary>Folders checked against the server if the copy here is older than this.</summary>
    public static readonly TimeSpan FreshEnough = TimeSpan.FromMinutes(3);

    public sealed record MailboxInfo(string Address, string Name, bool SendsInvoices, IReadOnlyList<FolderInfo> Folders);
    public sealed record FolderInfo(string Name, string Path, string Role, int Unread, int Total);
    public sealed record MailListItem(string Id, string Mailbox, string Folder, DateTimeOffset Date, string? From, string To, string Subject,
        string Preview, bool Unread, bool Flagged, bool Answered, bool HasAttachments);
    public sealed record SearchResult(IReadOnlyList<MailListItem> Mail, string? Note);
    public sealed record AttachmentInfo(string FileName, string ContentType, long Size);
    public sealed record MailContent(string Warning, string Id, string Mailbox, string Folder, DateTimeOffset Date, string? From,
        string To, string Cc, string Subject, string? InReplyTo, string Text, IReadOnlyList<AttachmentInfo> Attachments);
    public sealed record DraftSaved(string Mailbox, string Subject, string To, string Message);

    private IReadOnlyList<MailAccount> Visible() => AssistantMail.Visible(store.ListMailAccounts());

    public IReadOnlyList<MailboxInfo> ListMailboxes() =>
        Visible().Select(a => new MailboxInfo(a.Address, a.DisplayName, a.SendsInvoices,
            store.ListMailFolders(a.Id).Select(f => new FolderInfo(f.Name, f.Path, f.Role.ToString().ToLowerInvariant(), f.Unread, f.Total)).ToList()))
        .ToList();

    /// <summary>
    /// Searches mail. With no words it lists the newest mail in a folder (the inbox unless another is named).
    /// Folders are brought up to date from the server first when the copy on this PC is a few minutes old.
    /// </summary>
    public async Task<SearchResult> SearchAsync(string? query, string? mailbox, string? folder, bool unreadOnly, int limit,
        CancellationToken cancel = default, string? client = null)
    {
        ClientMail? withClient = null;
        if (!string.IsNullOrWhiteSpace(client))
        {
            var clients = store.ListClients();
            var match = clients.FirstOrDefault(c => c.Name.Equals(client.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? clients.FirstOrDefault(c => c.Name.Contains(client.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return new SearchResult([], $"No client called {client} in Webignite.");
            withClient = new ClientMail(match);
            if (withClient.IsEmpty)
                return new SearchResult([], $"{match.Name} has no email address or website in Webignite, so their mail can't be found by client. Search by name instead.");
        }
        var visible = Visible();
        if (visible.Count == 0)
            return new SearchResult([], "No mailboxes are shared with assistants. Eddie can allow it per mailbox in Webignite, under Mail, Manage mailboxes.");
        var accounts = visible;
        if (!string.IsNullOrWhiteSpace(mailbox))
        {
            if (AssistantMail.FindMailbox(visible, mailbox) is not { } one)
                return new SearchResult([], $"No shared mailbox called {mailbox}. Shared: {string.Join(", ", visible.Select(a => a.Address))}.");
            accounts = [one];
        }
        limit = Math.Clamp(limit <= 0 ? 20 : limit, 1, AssistantMail.MaxResults);

        var notes = new List<string>();
        var folders = accounts.SelectMany(a => PickFolders(a, folder)).ToList();
        if (folders.Count == 0)
            return new SearchResult([], $"No folder called {folder}. Use list_mailboxes to see the folders.");
        foreach (var (account, path) in folders)
            await Refresh(account, path, notes, cancel);

        IEnumerable<MailSummary> found = withClient is not null
            ? store.FindMail(accounts.Select(a => a.Id), s => withClient.Matches(s)
                && (string.IsNullOrWhiteSpace(folder) || folders.Any(f => f.Account.Id == s.AccountId && f.Path == s.Folder))
                && (string.IsNullOrWhiteSpace(query) || query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => MailSearch.Matches(s, w))), 500)
            : string.IsNullOrWhiteSpace(query)
            ? folders.SelectMany(f => store.ListMailSummaries(f.Account.Id, f.Path))
            : store.SearchMail(accounts.Select(a => a.Id), query.Trim(), 500)
                .Where(s => string.IsNullOrWhiteSpace(folder) || folders.Any(f => f.Account.Id == s.AccountId && f.Path == s.Folder));
        if (unreadOnly)
            found = found.Where(s => !s.Seen);
        var list = found.OrderByDescending(s => s.Date).Take(limit)
            .Select(s => Item(s, accounts.First(a => a.Id == s.AccountId)))
            .ToList();
        return new SearchResult(list, notes.Count == 0 ? null : string.Join(" ", notes.Distinct()));
    }

    public async Task<MailContent> ReadAsync(string id, CancellationToken cancel = default)
    {
        var (account, summary) = Find(id);
        var mime = await mail.GetMessageAsync(account, summary.Folder, summary.Uid, cancel);
        var body = MailReader.Read(mime, allowRemoteImages: false);
        return new MailContent(
            AssistantMail.Warning,
            AssistantMail.Id(summary),
            account.Address,
            FolderName(account, summary.Folder),
            summary.Date,
            summary.From?.ToString(),
            Recipients.Format(summary.To),
            Recipients.Format(summary.Cc),
            summary.Subject,
            summary.InReplyTo,
            AssistantMail.Trim(body.Text),
            body.Attachments.Select(a => new AttachmentInfo(a.FileName, a.ContentType, a.Size)).ToList());
    }

    /// <summary>
    /// Saves a draft in a mailbox's Drafts folder. As a reply, it is threaded and addressed like Reply
    /// (or Reply all), quoting the original. The mailbox's signature is added. Nothing is sent.
    /// </summary>
    public async Task<DraftSaved> CreateDraftAsync(string? mailbox, string? replyToId, bool replyAll, IReadOnlyList<string>? to,
        IReadOnlyList<string>? cc, string? subject, string body, string draftedBy, CancellationToken cancel = default)
    {
        var (account, draft) = await BuildDraftAsync(mailbox, replyToId, replyAll, to, cc, subject, body, cancel);
        var message = MailComposer.Build(draft, account);
        message.Headers.Add(MailComposer.DraftedByHeader, draftedBy);
        await mail.SaveDraftAsync(account, message, cancel: cancel);
        return new DraftSaved(account.Address, draft.Subject, Recipients.Format(draft.To),
            $"Saved in the Drafts folder of {account.Address}. It has not been sent: Eddie reads it and sends it from Webignite or the phone.");
    }

    internal async Task<(MailAccount Account, OutgoingMail Draft)> BuildDraftAsync(string? mailbox, string? replyToId, bool replyAll,
        IReadOnlyList<string>? to, IReadOnlyList<string>? cc, string? subject, string body, CancellationToken cancel = default)
    {
        var visible = Visible();
        MailAccount account;
        OutgoingMail draft;
        if (!string.IsNullOrWhiteSpace(replyToId))
        {
            var (original, summary) = Find(replyToId);
            account = string.IsNullOrWhiteSpace(mailbox) ? original
                : AssistantMail.FindMailbox(visible, mailbox) ?? throw new AssistantException($"No shared mailbox called {mailbox}.");
            var mime = await mail.GetMessageAsync(original, summary.Folder, summary.Uid, cancel);
            draft = MailComposer.Reply(mime, account, store.ListMailAccounts().Select(a => a.Address).ToList(), replyAll) with { AccountId = account.Id };
        }
        else
        {
            account = AssistantMail.FindMailbox(visible, mailbox)
                ?? (visible.Count == 1 && string.IsNullOrWhiteSpace(mailbox) ? visible[0] : throw new AssistantException(
                    visible.Count == 0 ? "No mailboxes are shared with assistants."
                    : $"Say which mailbox to save the draft in: {string.Join(" or ", visible.Select(a => a.Address))}."));
            draft = new OutgoingMail { AccountId = account.Id };
        }

        var parsedTo = Recipients.Parse(string.Join(", ", to ?? []));
        var parsedCc = Recipients.Parse(string.Join(", ", cc ?? []));
        if (AssistantMail.CheckDraft(body, [.. parsedTo.Bad, .. parsedCc.Bad]) is { Count: > 0 } problems)
            throw new AssistantException(string.Join(" ", problems));

        return (account, draft with
        {
            To = to is { Count: > 0 } ? parsedTo.People : draft.To,
            Cc = cc is { Count: > 0 } ? parsedCc.People : draft.Cc,
            Subject = string.IsNullOrWhiteSpace(subject) ? draft.Subject : subject.Trim(),
            Body = body.TrimEnd() + Replies.StartBody(account.Signature),
        });
    }

    private (MailAccount Account, MailSummary Summary) Find(string id)
    {
        if (AssistantMail.ParseId(id) is not { } parsed)
            throw new AssistantException("That isn't a mail id from search_mail.");
        var account = Visible().FirstOrDefault(a => a.Id == parsed.AccountId)
            ?? throw new AssistantException("That mail is in a mailbox that isn't shared with assistants.");
        var summary = store.GetMailSummary(account.Id, parsed.Folder, parsed.Uid)
            ?? throw new AssistantException("That mail is no longer there. Search again.");
        return (account, summary);
    }

    private IEnumerable<(MailAccount Account, string Path)> PickFolders(MailAccount account, string? folder)
    {
        var folders = store.ListMailFolders(account.Id);
        if (string.IsNullOrWhiteSpace(folder))
            return [(account, folders.FirstOrDefault(f => f.Role == FolderRole.Inbox)?.Path ?? "INBOX")];
        var wanted = folder.Trim();
        return folders
            .Where(f => f.Path.Equals(wanted, StringComparison.OrdinalIgnoreCase) || f.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || f.Role.ToString().Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .Select(f => (account, f.Path));
    }

    private async Task Refresh(MailAccount account, string path, List<string> notes, CancellationToken cancel)
    {
        var known = store.ListMailFolders(account.Id).FirstOrDefault(f => f.Path == path);
        if (known?.Synced is { } synced && clock.GetUtcNow() - synced < FreshEnough)
            return;
        try
        {
            await mail.SyncFolderAsync(account, path, cancel: cancel);
        }
        catch (MailException e)
        {
            notes.Add($"Couldn't check {account.Address} for new mail ({e.Message}), so this is the copy on Eddie's PC.");
        }
    }

    private string FolderName(MailAccount account, string path) =>
        store.ListMailFolders(account.Id).FirstOrDefault(f => f.Path == path)?.Name ?? path;

    private MailListItem Item(MailSummary s, MailAccount account) => new(
        AssistantMail.Id(s), account.Address, FolderName(account, s.Folder), s.Date, s.From?.ToString(), Recipients.Format(s.To),
        s.Subject, s.Preview, !s.Seen, s.Flagged, s.Answered, s.HasAttachments);
}

/// <summary>A problem with what an assistant asked for, worded so it can correct itself.</summary>
public sealed class AssistantException(string message) : Exception(message);
