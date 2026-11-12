using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Snooze: the picked mail moves to a hidden Snoozed folder on the server until its time, then comes back to the
/// folder it came from, unread and at the top of the list. The rules for when are in Core.Mail.Snooze.
/// </summary>
public sealed partial class MailSnoozePart(AppServices services, MailViewModel page) : ViewModelBase
{
    /// <summary>The quick snooze times, then "Pick a date", for the toolbar button and the right-click menu.</summary>
    public IReadOnlyList<MailMenuItem> SnoozeMenu()
    {
        var mail = page.Targets();
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
        snoozePicking = page.Targets();
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
            page.SetStatus("That time has already passed. Pick a later one.", true);
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
        foreach (var (account, list) in page.ByMailbox(withId))
        {
            var ok = await page.Run(async () =>
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
            page.RemovedAndReload(moved);
            page.SetStatus($"Snoozed {MailViewModel.Count(moved.Count)} {Snooze.Until(until, DateTimeOffset.Now)}."
                + (skipped > 0 ? $" {MailViewModel.Count(skipped)} couldn't be snoozed, as the mail server gave no ID to find it again by." : ""), false);
        }
        else if (skipped > 0)
            page.SetStatus("This mail can't be snoozed: the mail server gave no ID to find it again by.", true);
    }

    /// <summary>When a mail counts as arriving for the list's order: mail back from snooze counts from when it came back.</summary>
    internal Func<MailSummary, DateTimeOffset> Arrival()
    {
        var woken = services.Settings.Snooze.Woken;
        var now = DateTimeOffset.Now;
        return woken.Count == 0 ? m => m.Date : m => Snooze.Arrived(m, woken, now);
    }
}
