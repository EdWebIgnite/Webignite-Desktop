using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Snooze: the picked mail moves to a hidden Snoozed folder on the server until its time, then comes back to the
/// folder it came from, unread and at the top of the list. The rules for when are in Core.Mail.Snooze.
/// </summary>
public partial class MailViewModel
{
    /// <summary>The quick snooze times, then "Pick a date", for the toolbar button and the right-click menu.</summary>
    public IReadOnlyList<MailMenuItem> SnoozeMenu()
    {
        var mail = Targets();
        var items = Snooze.Choices(DateTimeOffset.Now)
            .Select(c => new MailMenuItem(c.Label, () => SnoozeUntil(c.When, mail)) { IsEnabled = mail.Count > 0 })
            .ToList();
        items.Add(MailMenuItem.Separator);
        items.Add(new("Pick a date…", () =>
        {
            StartPickSnooze();
            return Task.CompletedTask;
        }) { IsEnabled = mail.Count > 0 });
        return items;
    }

    [ObservableProperty] public partial bool PickingSnooze { get; set; }
    [ObservableProperty] public partial DateTime? SnoozeDay { get; set; }
    [ObservableProperty] public partial int SnoozeHour { get; set; } = 8;

    public static IReadOnlyList<int> SnoozeHours { get; } = Enumerable.Range(6, 16).ToList();

    private IReadOnlyList<MailSummary> snoozePicking = [];

    private void StartPickSnooze()
    {
        snoozePicking = Targets();
        SnoozeDay = DateTime.Today.AddDays(2);
        SnoozeHour = 8;
        PickingSnooze = true;
    }

    [RelayCommand]
    private async Task ConfirmSnoozePick()
    {
        if (SnoozeDay is not { } day)
            return;
        if (Snooze.At(DateOnly.FromDateTime(day), SnoozeHour, DateTimeOffset.Now) is not { } when)
        {
            SetStatus("That time has already passed. Pick a later one.", true);
            return;
        }
        PickingSnooze = false;
        await SnoozeUntil(when, snoozePicking);
    }

    [RelayCommand]
    private void CancelSnoozePick() => PickingSnooze = false;

    /// <summary>Moves the mail to the Snoozed folder of its mailbox, making the folder the first time.</summary>
    private async Task SnoozeUntil(DateTimeOffset until, IReadOnlyList<MailSummary> mail)
    {
        var withId = mail.Where(m => !string.IsNullOrWhiteSpace(m.MessageId)).ToList();
        var skipped = mail.Count - withId.Count;
        var snoozed = new List<SnoozedMail>();
        var moved = new List<MailSummary>();
        foreach (var (account, list) in ByMailbox(withId))
        {
            var ok = await Run(async () =>
            {
                var folder = services.Store.ListMailFolders(account.Id).FirstOrDefault(Snooze.IsSnoozeFolder)
                    ?? await services.Mail.CreateFolderAsync(account, null, Snooze.FolderName);
                await services.Mail.MoveAsync(account, list, folder.Path);
            }, quiet: true);
            if (!ok)
                continue;
            moved.AddRange(list);
            snoozed.AddRange(list.Select(m => new SnoozedMail
            {
                AccountId = account.Id, MessageId = m.MessageId!.Trim(), BackTo = m.Folder, Until = until, Subject = m.Subject,
            }));
        }
        if (snoozed.Count > 0)
        {
            var settings = services.Settings.Snooze;
            services.Settings.Snooze = settings with { Snoozed = [.. settings.Snoozed, .. snoozed] };
            RemovedAndReload(moved);
            SetStatus($"Snoozed {Count(moved.Count)} {Snooze.Until(until, DateTimeOffset.Now)}."
                + (skipped > 0 ? $" {Count(skipped)} couldn't be snoozed, as the mail server gave no ID to find it again by." : ""), false);
        }
        else if (skipped > 0)
            SetStatus("This mail can't be snoozed: the mail server gave no ID to find it again by.", true);
    }

    /// <summary>
    /// Brings back snoozed mail whose time has come: back to its folder, unread, and at the top of the list.
    /// A snooze whose mail is no longer in the Snoozed folder (moved from a phone, say) is dropped.
    /// </summary>
    private async Task WakeSnoozed()
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
            if (!await Run(() => services.Mail.SyncFolderAsync(account, folder.Path), quiet: true))
                continue;
            var waiting = services.Store.ListMailSummaries(account.Id, folder.Path, 5000);
            foreach (var back in group.GroupBy(s => s.BackTo))
            {
                var found = back.Select(s => (Snooze: s, Mail: Snooze.Find(s, waiting))).ToList();
                done.UnionWith(found.Where(f => f.Mail is null).Select(f => f.Snooze));
                var mail = found.Where(f => f.Mail is not null).Select(f => f.Mail!).ToList();
                if (mail.Count == 0)
                    continue;
                var ok = await Run(async () =>
                {
                    var unread = await services.Mail.MarkAsync(account, mail, MailMark.Read, false);
                    await services.Mail.MoveAsync(account, unread.Count == mail.Count ? unread : mail, back.Key);
                }, quiet: true);
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

    /// <summary>When a mail counts as arriving for the list's order: mail back from snooze counts from when it came back.</summary>
    private Func<MailSummary, DateTimeOffset> Arrival()
    {
        var woken = services.Settings.Snooze.Woken;
        var now = DateTimeOffset.Now;
        return woken.Count == 0 ? m => m.Date : m => Snooze.Arrived(m, woken, now);
    }
}
