using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Core.Writing;
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

    private readonly Func<IReadOnlyList<MailPerson>, Task>? afterSent;

    public ComposeViewModel(AppServices services, MailViewModel owner, IReadOnlyList<MailAccount> accounts, OutgoingMail mail,
        byte[]? source = null, MailSummary? answering = null, Func<IReadOnlyList<MailPerson>, Task>? afterSent = null)
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
        ReplyTo = Recipients.Format(mail.ReplyTo);
        ShowCc = mail.Cc.Count + mail.Bcc.Count + mail.ReplyTo.Count > 0;
        Subject = mail.Subject;
        Document = Writing(mail, From);
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
    [ObservableProperty] public partial string ReplyTo { get; set; } = "";
    [ObservableProperty] public partial bool ShowCc { get; set; }
    [ObservableProperty] public partial string Subject { get; set; } = "";

    /// <summary>What is being written, signature included. The editor changes it in place and bumps <see cref="BodyRevision"/>.</summary>
    [ObservableProperty] public partial RichDocument Document { get; set; }

    [ObservableProperty] public partial int BodyRevision { get; set; }

    /// <summary>The writing as plain text, for checks such as a mentioned attachment.</summary>
    public string Body => RichText.ToText(Document);
    [ObservableProperty] public partial bool ShowQuote { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool MessageIsProblem { get; set; }
    [ObservableProperty] public partial bool IsWorking { get; set; }
    [ObservableProperty] public partial bool AskingAboutAttachment { get; set; }
    [ObservableProperty] public partial bool AskingToDiscard { get; set; }

    public bool HasMessage => Message.Length > 0;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>
    /// The writing a mail opens with: a draft written here comes back exactly; anything else is its text with
    /// the mailbox's signature under it (a draft from elsewhere already has its own).
    /// </summary>
    private static RichDocument Writing(OutgoingMail mail, MailAccount from)
    {
        if (mail.Html is { } html)
        {
            var images = mail.Images.ToDictionary(i => "cid:" + i.ContentId, i => (i.Data, i.ContentType));
            return RichHtml.Read(html, src => images.TryGetValue(src, out var found) ? found : RichHtml.DataAddress(src));
        }
        if (mail.DraftUid is not null)
            return RichText.FromText(mail.Body);
        return RichText.Starting(mail.Body, Signature(from));
    }

    private static IReadOnlyList<Block> Signature(MailAccount account) =>
        RichText.Signature(account.Signature, account.SignatureImage, account.SignatureImageType);

    /// <summary>Switching mailbox swaps the signature too, wherever it sits; an untouched new mail just gets the new one.</summary>
    partial void OnFromChanged(MailAccount oldValue, MailAccount newValue)
    {
        if (oldValue is null || Document is null)
            return;
        var writing = Document.Clone();
        if (!RichEditing.SwapSignature(writing, Signature(newValue)))
        {
            if (!writing.IsBlank)
                return;
            writing = RichText.Starting(null, Signature(newValue));
        }
        Document = writing;
    }

    partial void OnBodyRevisionChanged(int value) => OnPropertyChanged(nameof(HasChanges));

    /// <summary>Whether anything has changed since it was opened or last saved.</summary>
    public bool HasChanges => State() != savedState;

    private string State() => string.Join('\u0001', From.Id, To, Cc, Bcc, ReplyTo, Subject, RichHtml.Write(Document, PictureId),
        string.Join('|', Attachments.Select(a => a.Name)));

    // The same picture keeps the same id through undo and copies, which share its bytes.
    private static readonly ConditionalWeakTable<byte[], string> PictureIds = new();

    private static string PictureId(PictureBlock picture) =>
        "cid:" + PictureIds.GetValue(picture.Data, _ => $"picture{Guid.NewGuid():N}@webignite");

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
        var replyTo = Recipients.Parse(ReplyTo);
        var pictures = Document.Blocks.OfType<PictureBlock>().GroupBy(p => PictureId(p)).Select(g => g.First());
        var mail = start with
        {
            AccountId = From.Id,
            To = to.People,
            Cc = cc.People,
            Bcc = bcc.People,
            ReplyTo = replyTo.People,
            Subject = Subject,
            Body = Body,
            Html = RichHtml.Write(Document, PictureId),
            Images = pictures.Select(p => new InlineImage(PictureId(p)[4..], p.Data, p.ContentType)).ToList(),
            Attachments = Attachments.Select(a => a.Attachment).ToList(),
            DraftUid = draftUid,
        };
        return (mail, [.. to.Bad, .. cc.Bad, .. bcc.Bad, .. replyTo.Bad]);
    }

    [RelayCommand]
    private Task Send() => SendNow(checkAttachment: true);

    [RelayCommand]
    private Task SendAnyway() => SendNow(checkAttachment: false);

    [RelayCommand]
    private void AddAttachmentFirst() => AskingAboutAttachment = false;

    /// <summary>Checks the mail, then hands it to the Mail page, which sends it after a few seconds unless Undo is clicked.</summary>
    private Task SendNow(bool checkAttachment)
    {
        if (IsWorking || Ready(checkAttachment) is not { } mail)
            return Task.CompletedTask;
        owner.QueueSend(this, Recipients.Short(mail.To.Concat(mail.Cc).Concat(mail.Bcc).ToList()), () => Deliver(mail));
        return Task.CompletedTask;
    }

    /// <summary>The mail as it stands when it is fit to send, else null with the problem shown or the attachment question asked.</summary>
    private OutgoingMail? Ready(bool checkAttachment)
    {
        var (mail, bad) = Current();
        var problems = OutgoingRules.CheckForSending(mail, bad).ToList();
        if (mail.Attachments.FirstOrDefault(a => a.FilePath is not null && !File.Exists(a.FilePath)) is { } missing)
            problems.Add($"{missing.FileName} is no longer on this PC.");
        if (problems.Count > 0)
        {
            Say(string.Join(" ", problems), true);
            return null;
        }
        if (checkAttachment && OutgoingRules.MentionsMissingAttachment(mail))
        {
            AskingAboutAttachment = true;
            return null;
        }
        AskingAboutAttachment = false;
        return string.IsNullOrWhiteSpace(mail.Subject) ? mail with { Subject = "" } : mail;
    }

    /// <summary>Sends the mail now. When the server says no, the editor comes back with the reason.</summary>
    internal async Task Deliver(OutgoingMail mail)
    {
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
                    await afterSent([.. mail.To, .. mail.Cc]);
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
            owner.Reopen(this);
            Say("Not sent: " + e.Message, true);
        }
        catch (IOException e)
        {
            owner.Reopen(this);
            Say("Not sent. Couldn't read an attachment: " + e.Message, true);
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
        if (await KeepAsync())
            owner.CloseComposer(this);
        else if (!MessageIsProblem)
            Say("Still saving. Try again in a moment.", false);
    }

    /// <summary>
    /// Saves the draft when it has changes. True once everything typed is in Drafts. False while another save or
    /// a send is still running, or when saving failed (the reason is showing).
    /// </summary>
    internal async Task<bool> KeepAsync()
    {
        if (!HasChanges)
            return true;
        await SaveDraft();
        return !HasChanges;
    }

    /// <summary>A problem to show in the bar, from the screen (a file that isn't a picture).</summary>
    public void Tell(string problem) => Say(problem, true);

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
