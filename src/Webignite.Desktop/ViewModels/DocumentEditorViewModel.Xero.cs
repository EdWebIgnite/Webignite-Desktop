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
    [NotifyPropertyChangedFor(nameof(CanCreate), nameof(CanUpdate), nameof(CanChangeDraft), nameof(CanDecideQuote), nameof(CanVoid), nameof(CanCredit))]
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

    public XeroWriteState XeroState => stored?.Xero.State ?? XeroWriteState.None;
    public bool IsEditable => XeroState is XeroWriteState.None or XeroWriteState.Editing;

    /// <summary>A draft already in Xero being changed here.</summary>
    public bool IsChangingInXero => XeroState == XeroWriteState.Editing;
    public bool IsVerified => XeroState == XeroWriteState.Verified;
    public bool HasXeroMessage => XeroMessage.Length > 0;
    public bool HasContactQuestion => ContactQuestion is not null;

    /// <summary>Why the Create button can't be used yet, or empty when it can.</summary>
    public string XeroHint =>
        services.XeroApi is null ? "Connect Xero in Settings first."
        : services.Settings.Xero.AccountCode is null ? "Pick the sales account in Settings first."
        : !IsReady ? "Finish the document first."
        : "";

    public bool CanCreate => XeroState == XeroWriteState.None && !XeroBusy && XeroHint.Length == 0;
    public bool ShowCreate => XeroState == XeroWriteState.None;
    public bool CanUpdate => IsChangingInXero && !XeroBusy && XeroHint.Length == 0;

    /// <summary>A draft Webignite put in Xero that nobody has approved or emailed: Change this draft unlocks it.</summary>
    public bool CanChangeDraft => stored is not null && XeroEdits.CanChange(stored) && !XeroBusy;
    public bool ShowCheck => XeroState is XeroWriteState.Writing or XeroWriteState.Uncertain or XeroWriteState.CreatedUnverified or XeroWriteState.Mismatch;
    public bool ShowStartOver => XeroState is XeroWriteState.Writing or XeroWriteState.Uncertain && stored?.Xero.Id is null;
    public bool HasXeroLink => Kind == DocumentKind.Invoice && stored?.Xero.Id is not null;

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

    /// <summary>Unlocks a draft that's in Xero, so it can be changed here and then updated in Xero.</summary>
    [RelayCommand]
    private void ChangeDraft()
    {
        if (stored is null)
            return;
        var changed = services.Documents.Change(stored.Id, XeroEdits.StartChanging);
        if (changed is null || changed.Xero.State != XeroWriteState.Editing)
        {
            stored = changed ?? stored;
            ShowXero("It can't be changed here any more: it was approved, emailed or paid meanwhile.", problem: true);
        }
        else
        {
            stored = changed;
            ShowXero("", problem: false);
            if (string.IsNullOrWhiteSpace(NumberText) && changed.Xero.Number is { } number)
                NumberText = number; // so the update keeps Xero's number
        }
        RefreshXero();
        OnPropertyChanged(nameof(NumberNote));
    }

    [RelayCommand]
    private async Task UpdateInXero()
    {
        if (!CanUpdate || services.XeroDrafts is not { } drafts || services.Settings.Xero.AccountCode is not { } account)
            return;
        Save();
        if (!IsChangingInXero)
            return;
        var target = new XeroTarget(account, BrandingThemeId: services.Settings.Xero.BrandingThemeId);
        await Run(() => drafts.UpdateAsync(stored!, target, ClientEmail(CustomerName)));
    }

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
        stored?.Xero.Id is { } id
            ? AppServices.OpenInBrowser(new Uri("https://go.xero.com/AccountsReceivable/View.aspx?InvoiceID=" + Uri.EscapeDataString(id)))
            : Task.CompletedTask;

    private async Task Create(string? contactId, bool createContact)
    {
        if (!CanCreate || services.XeroDrafts is not { } drafts || services.Settings.Xero.AccountCode is not { } account)
            return;
        if (createContact && ClientToCheck() is { } check)
        {
            ShowXero(check, problem: true);
            return;
        }
        Save();
        if (!IsEditable)
            return; // It went to Xero from another page meanwhile; Save said so.
        var target = new XeroTarget(account, BrandingThemeId: services.Settings.Xero.BrandingThemeId);
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
            if (stored is not null && services.Documents.Get(stored.Id) is { } latest)
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
        XeroWriteState.Editing => $"Changing draft {stored.Xero.Number} here. Xero still has the old copy until Update draft in Xero."
            + (stored.Xero.Problems.Count > 0 ? " Xero turned the last change down: " + string.Join(" ", stored.Xero.Problems) : ""),
        XeroWriteState.Writing => "Sending to Xero was cut off. Use Check Xero before anything else.",
        XeroWriteState.Uncertain => "It isn't clear whether Xero got it. " + string.Join(" ", stored.Xero.Problems),
        XeroWriteState.CreatedUnverified => $"In Xero as {stored.Xero.Number ?? "a draft"}, not compared yet. Use Check Xero.",
        XeroWriteState.Mismatch => $"In Xero as {stored.Xero.Number ?? "a draft"}, but it differs: " + string.Join(" ", stored.Xero.Problems),
        XeroWriteState.Verified when Kind == DocumentKind.Quote && stored.Settled?.As == Settlement.Accepted => $"The client accepted {stored.Xero.Number}.",
        XeroWriteState.Verified when Kind == DocumentKind.Quote && stored.Settled?.As == Settlement.Declined => $"The client declined {stored.Xero.Number}.",
        XeroWriteState.Verified when Kind == DocumentKind.Quote && stored.Settled?.As == Settlement.Invoiced => $"{stored.Xero.Number} has been invoiced.",
        XeroWriteState.Verified when Kind == DocumentKind.Quote && stored.Delivery.Emailed is not null =>
            $"{stored.Xero.Number} went to the client. Say whether they accepted or declined it when you hear.",
        XeroWriteState.Verified when stored.Delivery.Approved is not null => $"{stored.Xero.Number} is approved in Xero.",
        XeroWriteState.Verified => Kind == DocumentKind.Quote
            ? $"Draft {stored.Xero.Number} is in Xero and matches this document. Email it when it's right."
            : $"Draft {stored.Xero.Number} is in Xero and matches this document. Use Approve and email when it's right.",
        _ => stored.Xero.Problems.Count > 0 ? "Xero turned it down: " + string.Join(" ", stored.Xero.Problems) : "",
    };

    public bool HasXeroStatus => XeroStatus.Length > 0;
    public bool XeroStatusIsProblem => XeroState is not (XeroWriteState.Verified or XeroWriteState.CreatedUnverified)
        && !(XeroState == XeroWriteState.Editing && stored?.Xero.Problems.Count == 0);

    private void RefreshXero()
    {
        foreach (var name in new[]
        {
            nameof(XeroState), nameof(IsEditable), nameof(IsVerified), nameof(XeroHint), nameof(CanCreate), nameof(ShowCreate), nameof(ShowCheck),
            nameof(IsChangingInXero), nameof(CanUpdate), nameof(CanChangeDraft), nameof(CanDecideQuote), nameof(Links), nameof(HasLinks),
            nameof(CanVoid), nameof(VoidLabel), nameof(VoidQuestion), nameof(CanCredit), nameof(CreditHint), nameof(CreditNotesText), nameof(HasCreditNotes),
            nameof(ShowStartOver), nameof(HasXeroLink), nameof(XeroStatus), nameof(HasXeroStatus), nameof(XeroStatusIsProblem),
        })
            OnPropertyChanged(name);
        RefreshEmail();
    }

    /// <summary>The first address saved on the matching client, used when a new Xero contact is made.</summary>
    private string? ClientEmail(string customer)
    {
        return Webignite.Core.Mail.ClientMail.First(Webignite.Core.Clients.ClientDirectory.Find(services.Clients.List(), customer)?.Email);
    }
}

/// <summary>Emailing the document from Webignite's own invoice mailbox, with Xero's PDF attached.</summary>
public partial class DocumentEditorViewModel
{
    [ObservableProperty]
    public partial bool ConfirmingApprove { get; set; }

    public bool ShowEmail => stored is not null && XeroSending.CannotSend(stored) is null;

    public string EmailButtonText =>
        stored?.Delivery.Emailed is not null ? "Email again"
        : Kind == DocumentKind.Quote ? "Email the quote"
        : stored?.Delivery.Approved is null ? "Approve and email" : "Email the invoice";

    public string EmailedText => stored?.Delivery.Emailed is { } when
        ? (string.IsNullOrWhiteSpace(stored.Delivery.EmailedTo) ? "Emailed" : $"Emailed to {stored.Delivery.EmailedTo}") + $" on {when.ToLocalTime():d MMM} at {when.ToLocalTime():HH:mm}."
        : "";
    public bool HasEmailedText => EmailedText.Length > 0;

    public string ApproveQuestion =>
        $"This approves {stored?.Xero.Number ?? "the invoice"} in Xero, so it counts as owed, then opens the email from your invoice mailbox with Xero's PDF attached. Nothing is sent until you press Send.";

    /// <summary>
    /// When Claude or ChatGPT changed this client's email or name and Eddie hasn't said it's right yet, what to say
    /// instead of emailing or making the Xero contact; null when there's nothing to check.
    /// </summary>
    private string? ClientToCheck() =>
        Webignite.Core.Clients.ClientDirectory.Find(services.Clients.List(), CustomerName) is { Check: { } check } client
            ? $"{check.What} Open {client.Name}'s page and click It's right first, so nothing goes to the wrong address."
            : null;

    [RelayCommand]
    private Task StartEmail()
    {
        if (ClientToCheck() is { } check)
        {
            ShowXero(check, problem: true);
            return Task.CompletedTask;
        }
        if (Kind == DocumentKind.Invoice && stored?.Delivery.Approved is null)
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
        // The editor may have been open on another page while this changed (emailed, approved, paid).
        stored = services.Documents.Get(stored.Id) ?? stored;
        if (services.Drafts.InvoiceMailbox is not { } mailbox)
        {
            ShowXero(MailDrafts.NoInvoiceMailbox, problem: true);
            return;
        }

        XeroBusy = true;
        ShowXero(Kind == DocumentKind.Invoice && stored.Delivery.Approved is null ? "Approving in Xero and fetching the PDF…" : "Fetching the PDF from Xero…", problem: false);
        try
        {
            var ready = await sending.PrepareAsync(stored, services.Settings.Xero.TenantName ?? "Webignite", brandingThemeId: services.Settings.Xero.BrandingThemeId);
            stored = ready.Stored;
            var pdf = await services.Drafts.AttachAsync(ready.PdfName, ready.Pdf);
            var filled = DocumentEmail.Fill(services.Settings.EmailTemplates.For(Kind), ready.Values);
            var stamped = stored;
            var opened = services.Drafts.Open(new MailDraft(mailbox, ready.ContactEmail, filled.Subject, filled.Body)
            {
                Attachments = [pdf],
                NoAddress = $"Xero has no email address for {ready.Values.ContactName}. Type it in To.",
                Unfilled = filled.Unfilled,
                AfterSent = async sentTo =>
                {
                    stored = await sending.MarkSentAsync(stamped, sentTo);
                    RefreshXero();
                    RefreshEmail();
                },
            });
            ShowXero(opened ? "" : MailDrafts.AnotherOpen, problem: !opened);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            if (services.Documents.Get(stored.Id) is { } latest)
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

/// <summary>The client's answer to a quote, and the quote and invoices linked to this one.</summary>
public partial class DocumentEditorViewModel
{
    /// <summary>A link to the quote this invoice was made from, or an invoice made from this quote.</summary>
    public sealed record DocumentLink(string Text, StoredDocument Target);

    /// <summary>A sent quote with no answer yet: Client accepted and Client declined can be clicked.</summary>
    public bool CanDecideQuote => stored is not null && QuoteOutcomes.CanDecide(stored) && !XeroBusy;

    public IReadOnlyList<DocumentLink> Links
    {
        get
        {
            if (stored is null)
                return [];
            if (Kind == DocumentKind.Quote)
                return QuoteOutcomes.InvoicesFrom(stored, services.Documents.List(DocumentKind.Invoice))
                    .Select(i => new DocumentLink(i.Xero.Number is { } n ? "Invoiced as " + n : "Draft invoice made from it", i)).ToList();
            return stored.FromQuote is { } id && services.Documents.Get(id) is { } quote
                ? [new DocumentLink("Made from quote " + QuoteOutcomes.Name(quote), quote)]
                : [];
        }
    }

    public bool HasLinks => Links.Count > 0;

    [RelayCommand]
    private void OpenLinked(DocumentLink link) => services.Go.Document(services.Documents.Get(link.Target.Id) ?? link.Target);

    [RelayCommand]
    private Task AcceptQuote() => DecideQuote(accepted: true);

    [RelayCommand]
    private Task DeclineQuote() => DecideQuote(accepted: false);

    private async Task DecideQuote(bool accepted)
    {
        if (stored is null || services.XeroSending is not { } sending)
        {
            ShowXero("Connect Xero in Settings first.", problem: true);
            return;
        }
        stored = services.Documents.Get(stored.Id) ?? stored;
        if (!QuoteOutcomes.CanDecide(stored))
        {
            RefreshXero();
            return;
        }
        XeroBusy = true;
        ShowXero("Talking to Xero…", problem: false);
        try
        {
            stored = await sending.DecideQuoteAsync(stored, accepted);
            ShowXero(stored.Settled?.As switch
            {
                Settlement.Accepted => "Marked accepted in Xero. Use Make invoice when it's time to bill it.",
                Settlement.Declined => "Marked declined in Xero.",
                Settlement.Invoiced => "Xero already has it as invoiced.",
                _ => "Xero already had an answer for it.",
            }, problem: false);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            stored = services.Documents.Get(stored.Id) ?? stored;
            ShowXero("Couldn't tell Xero: " + e.Message, problem: true);
        }
        finally
        {
            XeroBusy = false;
            RefreshXero();
        }
    }
}
