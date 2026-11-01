using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Writing a mail, a reply or a forward. Nothing leaves until Send is pressed. Drafts are kept in the
/// mailbox's Drafts folder on the server, so they also show on your phone and other mail programs.
/// </summary>
public partial class ComposeViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly MailViewModel owner;
    private readonly OutgoingMail start;
    private readonly byte[]? source;
    private readonly MailSummary? answering;
    private uint? draftUid;
    private string savedState;

    private readonly Func<Task>? afterSent;

    public ComposeViewModel(AppServices services, MailViewModel owner, IReadOnlyList<MailAccount> accounts, OutgoingMail mail,
        byte[]? source = null, MailSummary? answering = null, Func<Task>? afterSent = null)
    {
        this.afterSent = afterSent;
        this.services = services;
        this.owner = owner;
        this.source = source;
        this.answering = answering;
        start = mail;
        draftUid = mail.DraftUid;
        Accounts = accounts;
        From = accounts.FirstOrDefault(a => a.Id == mail.AccountId) ?? accounts[0];
        To = Recipients.Format(mail.To);
        Cc = Recipients.Format(mail.Cc);
        Bcc = Recipients.Format(mail.Bcc);
        ShowCc = mail.Cc.Count + mail.Bcc.Count > 0;
        Subject = mail.Subject;
        Body = mail.Body;
        foreach (var attachment in mail.Attachments)
            Attachments.Add(new ComposeAttachmentViewModel(attachment, this));
        QuoteHtml = mail.Quote is { } quote
            ? MailStyle.Wrap("<div style=\"color:#5F6B66\">" + System.Net.WebUtility.HtmlEncode(quote.Header).Replace("\n", "<br>") + "</div>"
                + (quote.Html ?? MailHtml.FromText(quote.Text)))
            : "";
        Heading = mail.DraftUid is not null ? "Draft"
            : mail.Quote is { IsForward: true } ? "Forward"
            : mail.Quote is not null ? "Reply"
            : "New mail";
        savedState = State();
    }

    public IReadOnlyList<MailAccount> Accounts { get; }
    public bool ManyAccounts => Accounts.Count > 1;
    public string Heading { get; }
    public ObservableCollection<ComposeAttachmentViewModel> Attachments { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;
    public string QuoteHtml { get; }
    public bool HasQuote => QuoteHtml.Length > 0;

    [ObservableProperty] public partial MailAccount From { get; set; }
    [ObservableProperty] public partial string To { get; set; } = "";
    [ObservableProperty] public partial string Cc { get; set; } = "";
    [ObservableProperty] public partial string Bcc { get; set; } = "";
    [ObservableProperty] public partial bool ShowCc { get; set; }
    [ObservableProperty] public partial string Subject { get; set; } = "";
    [ObservableProperty] public partial string Body { get; set; } = "";
    [ObservableProperty] public partial bool ShowQuote { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool MessageIsProblem { get; set; }
    [ObservableProperty] public partial bool IsWorking { get; set; }
    [ObservableProperty] public partial bool AskingAboutAttachment { get; set; }
    [ObservableProperty] public partial bool AskingToDiscard { get; set; }

    public bool HasMessage => Message.Length > 0;

    private (string Id, Avalonia.Media.Imaging.Bitmap? Picture)? picture;

    /// <summary>The From mailbox's signature picture, shown under the text while the signature is in it.</summary>
    public Avalonia.Media.Imaging.Bitmap? SignaturePicture
    {
        get
        {
            if (From.SignatureImage is not { Length: > 0 } bytes)
                return null;
            if (picture is not { } cached || cached.Id != From.Id)
            {
                Avalonia.Media.Imaging.Bitmap? bitmap = null;
                try
                {
                    using var stream = new MemoryStream(bytes);
                    bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
                {
                }
                picture = cached = (From.Id, bitmap);
            }
            return cached.Picture;
        }
    }

    public bool ShowSignaturePicture => SignatureImages.Belongs(From, Body) && SignaturePicture is not null;

    partial void OnBodyChanged(string value) => OnPropertyChanged(nameof(ShowSignaturePicture));
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>Switching mailbox swaps the signature too, if it hasn't been edited away.</summary>
    partial void OnFromChanged(MailAccount oldValue, MailAccount newValue)
    {
        OnPropertyChanged(nameof(SignaturePicture));
        OnPropertyChanged(nameof(ShowSignaturePicture));
        if (oldValue?.Signature is { Length: > 0 } old && Body.TrimEnd().EndsWith(old.TrimEnd(), StringComparison.Ordinal))
            Body = Body.TrimEnd()[..^old.TrimEnd().Length].TrimEnd() + Replies.StartBody(newValue.Signature);
        else if (oldValue is not null && Body.Trim().Length == 0)
            Body = Replies.StartBody(newValue.Signature);
    }

    /// <summary>Whether anything has changed since it was opened or last saved.</summary>
    public bool HasChanges => State() != savedState;

    private string State() => string.Join('\u0001', From.Id, To, Cc, Bcc, Subject, Body, string.Join('|', Attachments.Select(a => a.Name)));

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            if (!info.Exists || Attachments.Any(a => a.Attachment.FilePath == path))
                continue;
            Attachments.Add(new ComposeAttachmentViewModel(new OutgoingAttachment(info.Name, info.Length, FilePath: path), this));
        }
        AskingAboutAttachment = false;
        OnPropertyChanged(nameof(HasAttachments));
    }

    internal void Remove(ComposeAttachmentViewModel attachment)
    {
        Attachments.Remove(attachment);
        OnPropertyChanged(nameof(HasAttachments));
    }

    [RelayCommand]
    private void ToggleCc() => ShowCc = !ShowCc;

    [RelayCommand]
    private void ToggleQuote() => ShowQuote = !ShowQuote;

    private (OutgoingMail Mail, IReadOnlyList<string> Bad) Current()
    {
        var to = Recipients.Parse(To);
        var cc = Recipients.Parse(Cc);
        var bcc = Recipients.Parse(Bcc);
        var mail = start with
        {
            AccountId = From.Id,
            To = to.People,
            Cc = cc.People,
            Bcc = bcc.People,
            Subject = Subject,
            Body = Body,
            Attachments = Attachments.Select(a => a.Attachment).ToList(),
            DraftUid = draftUid,
        };
        return (mail, [.. to.Bad, .. cc.Bad, .. bcc.Bad]);
    }

    [RelayCommand]
    private Task Send() => SendNow(checkAttachment: true);

    [RelayCommand]
    private Task SendAnyway() => SendNow(checkAttachment: false);

    [RelayCommand]
    private void AddAttachmentFirst() => AskingAboutAttachment = false;

    private async Task SendNow(bool checkAttachment)
    {
        if (IsWorking)
            return;
        var (mail, bad) = Current();
        var problems = OutgoingRules.CheckForSending(mail, bad).ToList();
        if (mail.Attachments.FirstOrDefault(a => a.FilePath is not null && !File.Exists(a.FilePath)) is { } missing)
            problems.Add($"{missing.FileName} is no longer on this PC.");
        if (problems.Count > 0)
        {
            Say(string.Join(" ", problems), true);
            return;
        }
        if (checkAttachment && OutgoingRules.MentionsMissingAttachment(mail))
        {
            AskingAboutAttachment = true;
            return;
        }
        AskingAboutAttachment = false;
        if (string.IsNullOrWhiteSpace(mail.Subject))
            mail = mail with { Subject = "" };

        IsWorking = true;
        Say("Sending…", false);
        try
        {
            var message = MailComposer.Build(mail, From, source);
            var note = await services.Mail.SendAsync(From, message, answering?.AccountId == From.Id ? answering : null, draftUid);
            if (afterSent is not null)
            {
                try
                {
                    await afterSent();
                }
                catch (Exception e) when (e is Webignite.Xero.XeroException or HttpRequestException or InvalidOperationException)
                {
                    note = "Sent, but Xero wasn't told: " + e.Message + " Mark it as sent in Xero.";
                }
            }
            owner.Sent(this, note ?? $"Sent to {Recipients.Format(mail.To.Concat(mail.Cc).Concat(mail.Bcc).Select(p => p with { Name = null }))}.");
        }
        catch (MailException e)
        {
            Say(e.Message, true);
        }
        catch (IOException e)
        {
            Say("Couldn't read an attachment: " + e.Message, true);
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private async Task SaveDraft()
    {
        if (IsWorking)
            return;
        IsWorking = true;
        Say("Saving the draft…", false);
        try
        {
            var (mail, _) = Current();
            var message = MailComposer.Build(mail, From, source);
            var previous = draftUid;
            // A draft moved to another mailbox is saved there and removed from the first one.
            if (previous is not null && start.AccountId != From.Id && Accounts.FirstOrDefault(a => a.Id == start.AccountId) is { } first)
            {
                await services.Mail.DeleteDraftAsync(first, previous.Value);
                previous = null;
            }
            draftUid = await services.Mail.SaveDraftAsync(From, message, previous);
            savedState = State();
            Say($"Draft saved in {From.Address} at {DateTime.Now:HH:mm}.", false);
            owner.DraftSaved();
        }
        catch (MailException e)
        {
            Say(e.Message, true);
        }
        catch (IOException e)
        {
            Say("Couldn't read an attachment: " + e.Message, true);
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        if (HasChanges)
        {
            AskingToDiscard = true;
            return;
        }
        owner.CloseComposer(this);
    }

    [RelayCommand]
    private void KeepWriting() => AskingToDiscard = false;

    [RelayCommand]
    private void Discard() => owner.CloseComposer(this);

    [RelayCommand]
    private async Task SaveAndClose()
    {
        await SaveDraft();
        if (!MessageIsProblem)
            owner.CloseComposer(this);
    }

    private void Say(string message, bool problem)
    {
        Message = message;
        MessageIsProblem = problem;
    }
}

public partial class ComposeAttachmentViewModel(OutgoingAttachment attachment, ComposeViewModel owner) : ViewModelBase
{
    public OutgoingAttachment Attachment => attachment;
    public string Name => attachment.FileName;
    public string Size => AttachmentViewModel.FormatSize(attachment.Size);

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}
