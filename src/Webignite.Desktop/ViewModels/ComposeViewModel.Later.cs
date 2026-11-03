using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>Send later: the mail is saved as a draft and sent at the picked time, at a mail check while Webignite is open.</summary>
public partial class ComposeViewModel
{
    /// <summary>Invoice and quote mail goes straight away, since Xero is told it was sent.</summary>
    public bool CanSendLater => afterSent is null;

    [ObservableProperty] public partial bool PickingSendTime { get; set; }
    [ObservableProperty] public partial DateTime? SendDay { get; set; }
    [ObservableProperty] public partial int SendHour { get; set; } = 8;

    public IReadOnlyList<(string Label, DateTimeOffset When)> SendLaterChoices() => SendLater.Choices(DateTimeOffset.Now);

    public void StartPickSendTime()
    {
        SendDay = DateTime.Today.AddDays(1);
        SendHour = 8;
        PickingSendTime = true;
    }

    [RelayCommand]
    private void CancelSendLater() => PickingSendTime = false;

    [RelayCommand]
    private Task ConfirmSendLater()
    {
        if (SendDay is not { } day)
            return Task.CompletedTask;
        if (Snooze.At(DateOnly.FromDateTime(day), SendHour, DateTimeOffset.Now) is not { } when)
        {
            Say("That time has already passed. Pick a later one.", true);
            return Task.CompletedTask;
        }
        PickingSendTime = false;
        return SendAt(when);
    }

    /// <summary>Saves the mail as a draft and sets it to go at that time.</summary>
    public async Task SendAt(DateTimeOffset when)
    {
        if (!CanSendLater || IsWorking || Ready(checkAttachment: true) is not { } mail)
            return;
        await SaveDraft();
        if (MessageIsProblem || draftUid is not { } uid)
            return;
        var settings = services.Settings.SendLater;
        services.Settings.SendLater = settings with
        {
            Scheduled = [.. settings.Scheduled, new ScheduledSend
            {
                Id = ScheduledSend.NewId(), AccountId = From.Id, DraftUid = uid, SendAt = when,
                To = Recipients.Short(mail.To.Concat(mail.Cc).Concat(mail.Bcc).ToList()), Subject = mail.Subject,
            }],
        };
        owner.Sent(this, $"Set to send to {Recipients.Short(mail.To)} {SendLater.When(when, DateTimeOffset.Now)}. It waits in Drafts until then; Webignite needs to be open, or it goes when Webignite next starts.");
    }
}
