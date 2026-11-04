using Webignite.Core.Mail;
using Webignite.Data;
using Webignite.Mail;

namespace Webignite.Desktop;

/// <summary>
/// The mail check that runs every few minutes, whether or not the Mail page is open: new mail in every
/// mailbox, mail set to send later, snoozed mail coming back, and the rules on what arrived.
/// </summary>
public sealed class MailCheck(AppServices services)
{
    private bool running;

    /// <summary>
    /// Checks every mailbox. Returns what went wrong, one line per problem (empty when all went well), or null
    /// when a check was already running.
    /// </summary>
    public async Task<IReadOnlyList<string>?> RunAsync()
    {
        if (running)
            return null;
        running = true;
        try
        {
            var accounts = services.Store.ListMailAccounts();
            var failed = new List<string>();
            foreach (var account in accounts)
            {
                if (!services.Mail.HasPassword(account))
                {
                    failed.Add($"{account.Address}: password not saved yet");
                    continue;
                }
                await Try(() => services.Mail.SyncAccountAsync(account), e => failed.Add($"{account.Address}: {e}"));
            }
            await SendScheduled(accounts);
            await WakeSnoozed(accounts);
            await RunRulesOnArrival(accounts, failed);
            return failed;
        }
        finally
        {
            running = false;
        }
    }

    // A dropped connection or a server's no is a problem to report, never a crash.
    private static async Task<bool> Try(Func<Task> job, Action<string>? onError = null)
    {
        try
        {
            await job();
            return true;
        }
        catch (MailException e)
        {
            onError?.Invoke(e.Message);
            if (onError is null)
                AppLog.Write("Checking mail", e);
            return false;
        }
    }


    /// <summary>Sends mail whose time has come. A draft that has gone from Drafts is dropped with a note.</summary>
    private async Task SendScheduled(IReadOnlyList<MailAccount> accounts)
    {
        var due = SendLater.Due(services.Settings.SendLater.Scheduled, DateTimeOffset.Now);
        foreach (var item in due)
        {
            var account = accounts.FirstOrDefault(a => a.Id == item.AccountId);
            var drafts = account is null ? null : services.Store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == FolderRole.Drafts);
            var sent = false;
            // Drafts is read at every check, so a draft missing from it was sent or deleted elsewhere.
            var gone = account is null || drafts is null || !services.Store.KnownMailUids(account.Id, drafts.Path).Contains(item.DraftUid);
            if (!gone)
            {
                try
                {
                    var mime = await services.Mail.GetMessageAsync(account!, drafts!.Path, item.DraftUid);
                    using var stream = new MemoryStream(mime);
                    var message = await MimeKit.MimeMessage.LoadAsync(stream);
                    message.Date = DateTimeOffset.Now;
                    await services.Mail.SendAsync(account!, message, null, item.DraftUid);
                    sent = true;
                }
                catch (MailException e)
                {
                    // Most likely no internet: it is tried again at the next check.
                    AppLog.Write("Sending a mail set for later", e);
                }
            }
            if (!sent && !gone)
                continue;
            services.Settings.SendLater = services.Settings.SendLater with
            {
                Scheduled = services.Settings.SendLater.Scheduled.Where(s => s.Id != item.Id).ToList(),
            };
            services.ShowNote?.Invoke(sent ? $"Sent to {item.To}" : "A mail set for later wasn't sent",
                sent ? $"\"{item.Subject}\" went at {DateTime.Now:HH:mm}, as set." : $"\"{item.Subject}\" was no longer in Drafts.", null);
        }
    }


    /// <summary>
    /// Brings back snoozed mail whose time has come: back to its folder, unread, and at the top of the list.
    /// A snooze whose mail is no longer in the Snoozed folder (moved from a phone, say) is dropped.
    /// </summary>
    private async Task WakeSnoozed(IReadOnlyList<MailAccount> accounts)
    {
        var settings = services.Settings.Snooze;
        var now = DateTimeOffset.Now;
        var due = Snooze.Due(settings.Snoozed, now);
        var done = new HashSet<SnoozedMail>();
        var woken = new List<WokenMail>();
        foreach (var group in due.GroupBy(s => s.AccountId))
        {
            var account = accounts.FirstOrDefault(a => a.Id == group.Key);
            var folder = account is null ? null : services.Store.ListMailFolders(account.Id).FirstOrDefault(Snooze.IsSnoozeFolder);
            if (account is null || folder is null)
            {
                done.UnionWith(group);
                continue;
            }
            if (!await Try(() => services.Mail.SyncFolderAsync(account, folder.Path)))
                continue;
            var waiting = services.Store.ListMailSummaries(account.Id, folder.Path, 5000);
            foreach (var back in group.GroupBy(s => s.BackTo))
            {
                var found = back.Select(s => (Snooze: s, Mail: Snooze.Find(s, waiting))).ToList();
                done.UnionWith(found.Where(f => f.Mail is null).Select(f => f.Snooze));
                var mail = found.Where(f => f.Mail is not null).Select(f => f.Mail!).ToList();
                if (mail.Count == 0)
                    continue;
                var ok = await Try(async () =>
                {
                    var unread = await services.Mail.MarkAsync(account, mail, MailMark.Read, false);
                    await services.Mail.MoveAsync(account, unread.Count == mail.Count ? unread : mail, back.Key);
                });
                if (!ok)
                    continue;
                done.UnionWith(found.Where(f => f.Mail is not null).Select(f => f.Snooze));
                woken.AddRange(mail.Select(m => new WokenMail(account.Id, m.MessageId!.Trim(), now)));
            }
        }
        if (done.Count == 0 && woken.Count == 0 && settings.Woken.Count == Snooze.Tidy(settings.Woken, now).Count)
            return;
        // Read the list again: mail snoozed while this ran must stay on it.
        var latest = services.Settings.Snooze;
        services.Settings.Snooze = new SnoozeSettings
        {
            Snoozed = latest.Snoozed.Where(s => !done.Contains(s)).ToList(),
            Woken = [.. Snooze.Tidy(latest.Woken, now), .. woken],
        };
    }


    /// <summary>Runs the rules on new inbox mail in every mailbox. The first look at a mailbox only notes where its inbox is up to.</summary>
    private async Task RunRulesOnArrival(IReadOnlyList<MailAccount> accounts, List<string> failed)
    {
        var settings = services.Settings.MailRules;
        var marks = settings.RanUpTo.ToDictionary();
        var outcomes = new List<RuleOutcome>();
        foreach (var account in accounts)
        {
            if (services.Store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == FolderRole.Inbox) is not { } inbox)
                continue;
            var (fresh, ranUpTo) = MailRules.Arrived(services.Store.ListMailSummaries(account.Id, inbox.Path, 500),
                settings.RanUpTo.TryGetValue(account.Id, out var mark) ? mark : null);
            marks[account.Id] = ranUpTo;
            outcomes.AddRange(MailRules.Apply(settings.Rules, fresh));
        }
        services.Settings.MailRules = services.Settings.MailRules with { RanUpTo = marks };
        if (outcomes.Count > 0)
            await CarryAsync(outcomes, e => failed.Add("A mail rule couldn't finish: " + e));
    }


    /// <summary>Does what the rules decided, one mailbox at a time: marks and categories first, then the moves.</summary>
    public async Task CarryAsync(IReadOnlyList<RuleOutcome> outcomes, Action<string> onError)
    {
        var accounts = services.Store.ListMailAccounts();
        foreach (var group in outcomes.GroupBy(o => o.Mail.AccountId))
        {
            if (accounts.FirstOrDefault(a => a.Id == group.Key) is not { } account)
                continue;
            var list = group.ToList();
            await Try(async () =>
            {
                if (list.Where(o => o.MarkRead).Select(o => o.Mail).ToList() is { Count: > 0 } read)
                    await services.Mail.MarkAsync(account, read, MailMark.Read, true);
                if (list.Where(o => o.MarkImportant).Select(o => o.Mail).ToList() is { Count: > 0 } important)
                    await services.Mail.MarkAsync(account, important, MailMark.Important, true);
                foreach (var keyword in list.SelectMany(o => o.Categories).Distinct(StringComparer.OrdinalIgnoreCase))
                    await services.Mail.SetCategoryAsync(account, list.Where(o => o.Categories.Contains(keyword, StringComparer.OrdinalIgnoreCase)).Select(o => o.Mail).ToList(), keyword, true);
                foreach (var move in list.Where(o => o.MoveTo is not null).GroupBy(o => o.MoveTo!))
                    await services.Mail.MoveAsync(account, move.Select(o => o.Mail).ToList(), move.Key);
            }, onError);
        }
    }
}
