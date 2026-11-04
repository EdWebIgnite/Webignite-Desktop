using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Data;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Sending: after Send a mail waits a few seconds so it can be taken back, and mail set to send later goes out
/// at the first mail check after its time.
/// </summary>
public partial class MailViewModel
{
    private (ComposeViewModel Composer, Func<Task> Send, string To)? pending;
    private DispatcherTimer? countdown;
    private int secondsLeft;

    [ObservableProperty] public partial string PendingSendText { get; set; } = "";
    public bool HasPendingSend => pending is not null;

    /// <summary>Closes the editor and sends after <see cref="SendLater.UndoSeconds"/>, unless Undo is clicked first.</summary>
    internal void QueueSend(ComposeViewModel composer, string to, Func<Task> send)
    {
        // Only one mail waits at a time: one already waiting goes now.
        if (pending is { } earlier)
            Release(earlier.Send);
        pending = (composer, send, to);
        CloseComposer(composer);
        secondsLeft = SendLater.UndoSeconds;
        PendingSendText = SendLater.Countdown(to, secondsLeft);
        OnPropertyChanged(nameof(HasPendingSend));
        services.ShowNote?.Invoke($"Sending to {to}", "Click here to undo.", () => UndoSendCommand.Execute(null));
        countdown?.Stop();
        countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) => Tick();
        countdown.Start();
    }

    private void Tick()
    {
        if (pending is not { } waiting)
        {
            countdown?.Stop();
            return;
        }
        secondsLeft--;
        if (secondsLeft > 0)
        {
            PendingSendText = SendLater.Countdown(waiting.To, secondsLeft);
            return;
        }
        Release(waiting.Send);
    }

    private void Release(Func<Task> send)
    {
        countdown?.Stop();
        pending = null;
        PendingSendText = "";
        OnPropertyChanged(nameof(HasPendingSend));
        send().Forget("Sending a mail");
    }

    /// <summary>
    /// Before the app closes: a mail still in its undo wait is sent now, and the open editor is saved to Drafts.
    /// Returns what couldn't be done, or null when nothing is left behind.
    /// </summary>
    internal async Task<string?> PrepareToCloseAsync()
    {
        if (pending is { } waiting)
        {
            countdown?.Stop();
            pending = null;
            PendingSendText = "";
            OnPropertyChanged(nameof(HasPendingSend));
            await waiting.Send(); // a refused mail comes back in the editor, which is saved below
        }
        if (Composer is { } open && !await open.KeepAsync())
            return $"The mail you're writing couldn't be saved to Drafts. {open.Message}".TrimEnd();
        return null;
    }

    /// <summary>Takes the waiting mail back and opens it in the editor again, as it was.</summary>
    [RelayCommand]
    private void UndoSend()
    {
        if (pending is not { } waiting)
            return;
        countdown?.Stop();
        pending = null;
        PendingSendText = "";
        OnPropertyChanged(nameof(HasPendingSend));
        Reopen(waiting.Composer);
        SetStatus("Not sent. The mail is back in the editor.", false);
    }

    /// <summary>Opens an editor again, e.g. after Undo or when the server refused the mail.</summary>
    internal void Reopen(ComposeViewModel composer) => Composer = composer;

    /// <summary>Sends mail whose time has come. A draft that has gone from Drafts is dropped with a note.</summary>
    private async Task SendScheduled()
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

    /// <summary>Takes a draft off the send-later list, e.g. when it is opened to edit or Don't send is clicked.</summary>
    internal void Unschedule(string accountId, uint draftUid)
    {
        var settings = services.Settings.SendLater;
        if (SendLater.For(settings.Scheduled, accountId, draftUid) is not { } item)
            return;
        services.Settings.SendLater = settings with { Scheduled = settings.Scheduled.Where(s => s.Id != item.Id).ToList() };
    }
}
