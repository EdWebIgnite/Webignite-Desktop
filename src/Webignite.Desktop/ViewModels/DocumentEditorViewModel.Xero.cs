using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Xero;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Xero part of the editor: create the draft, pick the contact when it isn't obvious, and check
/// Xero after a write that may or may not have arrived. Nothing here retries a write by itself.
/// </summary>
public partial class DocumentEditorViewModel
{
    private IReadOnlyList<XeroContact> xeroContacts = [];

    public ObservableCollection<XeroContact> ContactChoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial bool XeroBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasXeroMessage))]
    public partial string XeroMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool XeroMessageIsProblem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContactQuestion))]
    public partial string? ContactQuestion { get; set; }

    [ObservableProperty]
    public partial bool OfferNewContact { get; set; }

    [ObservableProperty]
    public partial string ContactSearch { get; set; } = "";

    public XeroWriteState XeroState => stored?.XeroState ?? XeroWriteState.None;
    public bool IsEditable => XeroState == XeroWriteState.None;
    public bool IsVerified => XeroState == XeroWriteState.Verified;
    public bool HasXeroMessage => XeroMessage.Length > 0;
    public bool HasContactQuestion => ContactQuestion is not null;

    /// <summary>Why the Create button can't be used yet, or empty when it can.</summary>
    public string XeroHint =>
        services.XeroApi is null ? "Connect Xero in Settings first."
        : services.XeroSettings.AccountCode is null ? "Pick the sales account in Settings first."
        : !IsReady ? "Finish the document first."
        : "";

    public bool CanCreate => IsEditable && !XeroBusy && XeroHint.Length == 0;
    public bool ShowCreate => IsEditable;
    public bool ShowCheck => XeroState is XeroWriteState.Writing or XeroWriteState.Uncertain or XeroWriteState.CreatedUnverified or XeroWriteState.Mismatch;
    public bool ShowStartOver => XeroState is XeroWriteState.Writing or XeroWriteState.Uncertain && stored?.XeroId is null;
    public bool HasXeroLink => Kind == DocumentKind.Invoice && stored?.XeroId is not null;

    partial void OnIsReadyChanged(bool value) => RefreshXero();

    partial void OnContactSearchChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        ContactChoices.Clear();
        foreach (var contact in XeroRules.Search(value, xeroContacts).Where(c => c.Active).Take(8))
            ContactChoices.Add(contact);
    }

    [RelayCommand]
    private Task CreateInXero() => Create(null, createContact: false);

    [RelayCommand]
    private Task UseContact(XeroContact contact) => Create(contact.Id, createContact: false);

    [RelayCommand]
    private Task AddNewContact() => Create(null, createContact: true);

    [RelayCommand]
    private async Task CheckXero()
    {
        if (stored is null || services.XeroDrafts is not { } drafts)
        {
            ShowXero("Connect Xero in Settings first.", problem: true);
            return;
        }
        await Run(() => drafts.CheckAsync(stored));
    }

    /// <summary>Only after a person looked in Xero and found nothing there.</summary>
    [RelayCommand]
    private void StartOver()
    {
        if (stored is null || services.XeroDrafts is not { } drafts)
            return;
        stored = drafts.StartOver(stored);
        ShowXero("", problem: false);
        RefreshXero();
    }

    [RelayCommand]
    private Task OpenInXero() =>
        stored?.XeroId is { } id
            ? AppServices.OpenInBrowser(new Uri("https://go.xero.com/AccountsReceivable/View.aspx?InvoiceID=" + Uri.EscapeDataString(id)))
            : Task.CompletedTask;

    private async Task Create(string? contactId, bool createContact)
    {
        if (!CanCreate || services.XeroDrafts is not { } drafts || services.XeroSettings.AccountCode is not { } account)
            return;
        Save();
        var target = new XeroTarget(account);
        var email = ClientEmail(CustomerName);
        await Run(() => drafts.CreateAsync(stored!, target, contactId, createContact, email));
    }

    private async Task Run(Func<Task<XeroDrafts.Result>> step)
    {
        XeroBusy = true;
        ShowXero("Talking to Xero…", problem: false);
        try
        {
            var result = await step();
            stored = result.Stored;
            ClearContactQuestion();
            switch (result)
            {
                case XeroDrafts.Result.Done:
                    ShowXero("", problem: false);
                    break;
                case XeroDrafts.Result.Stopped stopped:
                    ShowXero(stopped.Reason, problem: true);
                    break;
                case XeroDrafts.Result.NeedsContact needs:
                    ShowXero("", problem: false);
                    await AskForContact(needs.Match);
                    break;
            }
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Reads failed (or the check after a write). Whatever was saved along the way is in the store.
            if (stored is not null && services.Store.Get(stored.Id) is { } latest)
                stored = latest;
            ShowXero("Couldn't reach Xero: " + e.Message, problem: true);
        }
        finally
        {
            XeroBusy = false;
            RefreshXero();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(NumberNote));
        }
    }

    private async Task AskForContact(ContactMatch match)
    {
        if (services.XeroApi is { } api)
            xeroContacts = await api.ContactsAsync();
        var name = CustomerName.Trim();
        switch (match)
        {
            case ContactMatch.Choose choose:
                ContactQuestion = choose.Reason + " Which one is it?";
                foreach (var contact in choose.Candidates)
                    ContactChoices.Add(contact);
                break;
            case ContactMatch.Archived archived:
                ContactQuestion = $"{archived.Contact.Name} is archived in Xero. Restore them in Xero and try again, or search for another contact.";
                break;
            default:
                ContactQuestion = $"{name} isn't in Xero yet. Add them as a new contact, or search in case they are there under another name.";
                OfferNewContact = true;
                break;
        }
    }

    private void ClearContactQuestion()
    {
        ContactQuestion = null;
        OfferNewContact = false;
        ContactSearch = "";
        ContactChoices.Clear();
    }

    private void ShowXero(string message, bool problem)
    {
        XeroMessage = message;
        XeroMessageIsProblem = problem;
    }

    /// <summary>What to say about the document's place in Xero, from what was saved on this PC.</summary>
    public string XeroStatus => stored is null ? "" : XeroState switch
    {
        XeroWriteState.Writing => "Sending to Xero was cut off. Use Check Xero before anything else.",
        XeroWriteState.Uncertain => "It isn't clear whether Xero got it. " + string.Join(" ", stored.XeroProblems),
        XeroWriteState.CreatedUnverified => $"In Xero as {stored.XeroNumber ?? "a draft"}, not compared yet. Use Check Xero.",
        XeroWriteState.Mismatch => $"In Xero as {stored.XeroNumber ?? "a draft"}, but it differs: " + string.Join(" ", stored.XeroProblems),
        XeroWriteState.Verified when stored.Approved is not null => $"{stored.XeroNumber} is approved in Xero.",
        XeroWriteState.Verified => Kind == DocumentKind.Quote
            ? $"Draft {stored.XeroNumber} is in Xero and matches this document. Email it when it's right."
            : $"Draft {stored.XeroNumber} is in Xero and matches this document. Use Approve and email when it's right.",
        _ => stored.XeroProblems.Count > 0 ? "Xero turned it down: " + string.Join(" ", stored.XeroProblems) : "",
    };

    public bool HasXeroStatus => XeroStatus.Length > 0;
    public bool XeroStatusIsProblem => XeroState is not (XeroWriteState.Verified or XeroWriteState.CreatedUnverified);

    private void RefreshXero()
    {
        foreach (var name in new[]
        {
            nameof(XeroState), nameof(IsEditable), nameof(IsVerified), nameof(XeroHint), nameof(CanCreate), nameof(ShowCreate), nameof(ShowCheck),
            nameof(ShowStartOver), nameof(HasXeroLink), nameof(XeroStatus), nameof(HasXeroStatus), nameof(XeroStatusIsProblem),
        })
            OnPropertyChanged(name);
        RefreshEmail();
    }

    /// <summary>The first address saved on the matching client, used when a new Xero contact is made.</summary>
    private string? ClientEmail(string customer)
    {
        var wanted = XeroRules.Normalise(customer);
        var client = services.Store.ListClients().FirstOrDefault(c => XeroRules.Normalise(c.Name) == wanted);
        return client?.Email?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }
}

/// <summary>Emailing the document from Webignite's own invoice mailbox, with Xero's PDF attached.</summary>
public partial class DocumentEditorViewModel
{
    [ObservableProperty]
    public partial bool ConfirmingApprove { get; set; }

    public bool ShowEmail => stored is not null && XeroSending.CannotSend(stored) is null;

    public string EmailButtonText =>
        stored?.Emailed is not null ? "Email again"
        : Kind == DocumentKind.Quote ? "Email the quote"
        : stored?.Approved is null ? "Approve and email" : "Email the invoice";

    public string EmailedText => stored?.Emailed is { } when ? $"Emailed on {when.ToLocalTime():d MMM} at {when.ToLocalTime():HH:mm}." : "";
    public bool HasEmailedText => EmailedText.Length > 0;

    public string ApproveQuestion =>
        $"This approves {stored?.XeroNumber ?? "the invoice"} in Xero, so it counts as owed, then opens the email from your invoice mailbox with Xero's PDF attached. Nothing is sent until you press Send.";

    [RelayCommand]
    private Task StartEmail()
    {
        if (Kind == DocumentKind.Invoice && stored?.Approved is null)
        {
            ConfirmingApprove = true;
            return Task.CompletedTask;
        }
        return PrepareEmail();
    }

    [RelayCommand]
    private Task ConfirmApprove()
    {
        ConfirmingApprove = false;
        return PrepareEmail();
    }

    [RelayCommand]
    private void CancelApprove() => ConfirmingApprove = false;

    private async Task PrepareEmail()
    {
        if (stored is null || services.XeroSending is not { } sending)
        {
            ShowXero("Connect Xero in Settings first.", problem: true);
            return;
        }
        if (services.Store.ListMailAccounts().FirstOrDefault(a => a.SendsInvoices) is not { } mailbox)
        {
            ShowXero("No mailbox is set up to send invoices. In Mail, Manage mailboxes, tick \"Invoices are sent from this mailbox\" on accounts@.", problem: true);
            return;
        }

        XeroBusy = true;
        ShowXero(Kind == DocumentKind.Invoice && stored.Approved is null ? "Approving in Xero and fetching the PDF…" : "Fetching the PDF from Xero…", problem: false);
        try
        {
            var ready = await sending.PrepareAsync(stored, services.XeroSettings.TenantName ?? "Webignite");
            stored = ready.Stored;
            var folder = Path.Combine(Webignite.Data.DataPaths.Root(), "outgoing");
            Directory.CreateDirectory(folder);
            var pdf = Path.Combine(folder, string.Join("_", ready.PdfName.Split(Path.GetInvalidFileNameChars())));
            await File.WriteAllBytesAsync(pdf, ready.Pdf);

            var filled = DocumentEmail.Fill(services.EmailTemplates.For(Kind), ready.Values);
            var to = Webignite.Core.Mail.Recipients.Parse(ready.ContactEmail);
            var mail = new Webignite.Core.Mail.OutgoingMail
            {
                AccountId = mailbox.Id,
                To = to.People,
                Subject = filled.Subject,
                Body = filled.Body + Webignite.Core.Mail.Replies.StartBody(mailbox.Signature),
                Attachments = [new Webignite.Core.Mail.OutgoingAttachment(ready.PdfName, ready.Pdf.Length, FilePath: pdf)],
            };
            var notes = new List<string>();
            if (to.People.Count == 0)
                notes.Add($"Xero has no email address for {ready.Values.ContactName}. Type it in To.");
            if (filled.Unfilled.Count > 0)
                notes.Add($"Couldn't fill {string.Join(", ", filled.Unfilled)}. Fix the text before sending.");

            var stamped = stored;
            var opened = services.ComposeMail?.Invoke(mail, notes.Count > 0 ? string.Join(" ", notes) : null, async () =>
            {
                stored = await sending.MarkSentAsync(stamped);
                RefreshXero();
                RefreshEmail();
            }) ?? false;
            ShowXero(opened ? "" : "Another mail is open in Mail with changes. Send or close it first, then try again.", problem: !opened);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            if (services.Store.Get(stored.Id) is { } latest)
                stored = latest;
            ShowXero("Couldn't get it ready: " + e.Message, problem: true);
        }
        finally
        {
            XeroBusy = false;
            RefreshXero();
            RefreshEmail();
        }
    }

    private void RefreshEmail()
    {
        foreach (var name in new[] { nameof(ShowEmail), nameof(XeroStatus), nameof(EmailButtonText), nameof(EmailedText), nameof(HasEmailedText), nameof(ApproveQuestion) })
            OnPropertyChanged(name);
    }
}
