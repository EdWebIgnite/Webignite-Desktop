using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Quotes and invoices finished here without Xero (Settings, New documents): Finish gives the number, the email goes
/// out with Webignite's own PDF, and payments and the client's answer are recorded here.
/// </summary>
public partial class DocumentEditorViewModel
{
    /// <summary>Finished here, or a draft that will be, as Settings says to and it isn't in Xero.</summary>
    public bool IsOwn => IsIssued
        || (services.Settings.Documents.OnItsOwn && XeroState == XeroWriteState.None && stored?.Document.Origin is null or DocumentOrigin.New);

    public bool IsIssued => stored?.Status == DocumentStatus.Issued;

    public bool ShowFinish => IsOwn && !IsIssued;
    public bool CanFinish => ShowFinish && IsReady && !XeroBusy;

    /// <summary>The number Finish gives when none is typed.</summary>
    public string NextNumber
    {
        get
        {
            var settings = services.Settings.Documents;
            return DocumentNumbers.Next(Kind, services.Documents.List(Kind), Kind == DocumentKind.Quote ? settings.NextQuoteNumber : settings.NextInvoiceNumber);
        }
    }

    /// <summary>What the empty number box shows: the number Finish will give, or that Xero numbers it.</summary>
    public string NumberPlaceholder => IsOwn ? NextNumber : "Xero's next";

    public string ReadyText => IsOwn ? "Everything adds up. Ready to finish." : "Everything adds up. Ready for Xero.";

    public string OwnStatus => stored is not null && IsIssued ? OwnDocuments.Standing(stored, DateOnly.FromDateTime(DateTime.Today)) : "";
    public bool HasOwnStatus => OwnStatus.Length > 0;

    public bool CanReopen => stored is not null && OwnDocuments.CanReopen(stored) && !XeroBusy;
    public bool CanMarkSent => stored is not null && OwnDocuments.CanMarkSent(stored) && !XeroBusy;
    public bool CanCancelOwn => stored is not null && OwnDocuments.CanCancel(stored) && !XeroBusy;

    /// <summary>Gives it its number and makes it the client's document. Nothing is sent and nothing goes to Xero.</summary>
    [RelayCommand]
    private void Finish()
    {
        if (!CanFinish)
            return;
        if (ClientToCheck() is { } check)
        {
            ShowXero(check, problem: true);
            return;
        }
        var number = NumberText.Trim().Length > 0 ? NumberText.Trim() : NextNumber;
        if (DocumentNumbers.Problem(number, stored, Kind, services.Documents.List(Kind)) is { } problem)
        {
            ShowXero(problem, problem: true);
            return;
        }
        Save();
        if (stored is null || !IsEditable)
            return;
        var finished = services.Documents.Change(stored.Id, s => OwnDocuments.CanIssue(s) ? OwnDocuments.Issue(s, number, DateTimeOffset.UtcNow) : null);
        if (finished is null)
        {
            ShowXero("It can't be finished here: it went to Xero or was finished from another page meanwhile.", problem: true);
            return;
        }
        Own(finished, ""); // the line under the buttons says where it stands
        NumberText = number;
        changed = false;
    }

    /// <summary>Back to a draft to change it, keeping its number. Only while the client hasn't had it.</summary>
    [RelayCommand]
    private void Reopen()
    {
        if (stored is null)
            return;
        var open = services.Documents.Change(stored.Id, s => OwnDocuments.CanReopen(s) ? OwnDocuments.Reopen(s) : null);
        Own(open, open is null ? "It can't be changed any more: it was sent or paid meanwhile." : "It's a draft again, with its number kept. Finish it when it's right.", open is null);
    }

    [RelayCommand]
    private void MarkSent()
    {
        if (stored is null)
            return;
        var sent = services.Documents.Change(stored.Id, s => OwnDocuments.CanMarkSent(s) ? OwnDocuments.MarkSent(s, DateTimeOffset.UtcNow) : null);
        Own(sent, sent is null ? "It was already sent, or is settled." : "", problem: sent is null);
    }

    /// <summary>Cancels an invoice finished here with nothing paid on it. Its number stays used. Undo puts it back.</summary>
    [RelayCommand]
    private void CancelOwn()
    {
        if (stored is null)
            return;
        var before = services.Documents.Latest(stored);
        var cancelled = services.Documents.Change(stored.Id, s => OwnDocuments.CanCancel(s) ? OwnDocuments.Cancel(s, DateTimeOffset.UtcNow) : null);
        Own(cancelled, cancelled is null ? "It can't be cancelled: something was paid on it." : "", cancelled is null);
        if (cancelled is not null)
            services.OfferUndo($"Cancelled {cancelled.Number}.", () =>
            {
                services.Documents.Change(before.Id, s => s with { Settled = before.Settled });
                if (services.Documents.Get(before.Id) is { } back)
                    Own(back, "");
            });
    }

    private void PayOwn(decimal? amount, DateOnly? day)
    {
        if (stored is null)
            return;
        if (OwnDocuments.PaymentProblem(stored, amount, day, DateOnly.FromDateTime(DateTime.Today)) is { } problem)
        {
            ShowXero(problem, problem: true);
            return;
        }
        RecordingPayment = false;
        var paid = services.Documents.Change(stored.Id, s => OwnDocuments.CanPay(s) ? OwnDocuments.Pay(s, amount!.Value, day!.Value, DateTimeOffset.UtcNow) : null);
        if (paid is null)
        {
            Own(null, "It can't take a payment now: it was paid or cancelled meanwhile.", problem: true);
            return;
        }
        Own(paid, "");
        services.OfferUndo($"Recorded {Money.Rand(amount!.Value)} on {paid.Number}.", () =>
        {
            if (services.Documents.Change(paid.Id, OwnDocuments.Unpay) is { } back)
                Own(back, "");
        });
    }

    private void DecideOwn(bool accepted)
    {
        if (stored is null)
            return;
        var decided = services.Documents.Change(stored.Id, s => OwnDocuments.CanDecide(s) ? OwnDocuments.Decide(s, accepted, DateTimeOffset.UtcNow) : null);
        Own(decided, decided is null ? "It already has an answer." : accepted ? "Use Make invoice when it's time to bill it." : "", problem: decided is null);
    }

    // Shows the document as changed and says what happened; when nothing changed, it is read again as it is now.
    private void Own(StoredDocument? changedTo, string message, bool problem = false)
    {
        if (changedTo is not null)
            stored = changedTo;
        else if (stored is not null)
            stored = services.Documents.Latest(stored);
        ShowXero(message, problem);
        RefreshXero();
        SavedStamp++;
    }

    /// <summary>The email for a quote or invoice finished here, with Webignite's own PDF attached. Nothing goes until Send.</summary>
    private async Task PrepareOwnEmail()
    {
        if (stored is null)
            return;
        stored = services.Documents.Latest(stored);
        if (services.Drafts.InvoiceMailbox is not { } mailbox)
        {
            ShowXero(MailDrafts.NoInvoiceMailbox, problem: true);
            return;
        }
        XeroBusy = true;
        ShowXero("Making the PDF…", problem: false);
        try
        {
            var brand = services.Settings.MailBrand;
            var client = services.Clients.Find(stored.Document.Customer.Name);
            var sheet = DocumentSheets.Build(stored, brand, client);
            var bytes = await Task.Run(() => Webignite.Tools.DocumentPdfMaker.Make(sheet));
            var pdf = await services.Drafts.AttachAsync(sheet.FileName, bytes);
            var values = OwnEmailValues(stored, client, sheet.PayLink, brand);
            var filled = DocumentEmail.Fill(services.Settings.EmailTemplates.For(Kind), values);
            var stamped = stored;
            var address = services.Contacts.AddressFor(client, Kind == DocumentKind.Quote ? MailFor.Quotes : MailFor.Invoices)
                ?? Webignite.Core.Mail.ClientMail.First(client?.Email) ?? stored.Document.Customer.Email;
            var opened = services.Drafts.Open(new MailDraft(mailbox, address, filled.Subject, filled.Body)
            {
                Attachments = [pdf],
                NoAddress = $"There's no email address for {values.ContactName}. Type it in To.",
                Unfilled = filled.Unfilled,
                AfterSent = sentTo =>
                {
                    var to = sentTo is { Count: > 0 } ? Webignite.Core.Mail.Recipients.Short(sentTo) : null;
                    if (services.Documents.Change(stamped.Id, s => OwnDocuments.Emailed(s, to, DateTimeOffset.UtcNow)) is { } sent)
                        Own(sent, "");
                    return Task.CompletedTask;
                },
                Note = BrandedNote,
                Layout = () => DocumentMailLayout.Build(values, brand, AcceptTo(mailbox)),
            });
            ShowXero(opened ? "" : MailDrafts.AnotherOpen, problem: !opened);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Webignite.Data.AppLog.Write("Getting a quote or invoice mail ready", e);
            ShowXero("Couldn't get it ready: " + e.Message, problem: true);
        }
        finally
        {
            XeroBusy = false;
            RefreshXero();
        }
    }

    // What the email wording fills in, from the document itself rather than from Xero.
    private static DocumentEmailValues OwnEmailValues(StoredDocument s, Client? client, string? payLink, MailBrand brand) => new()
    {
        Kind = s.Document.Kind,
        ContactName = client?.Called ?? s.Document.Customer.Name.Trim(),
        FirstName = (client?.ContactPerson ?? (client?.Called != client?.Name ? client?.Name : null))?.Trim().Split(' ')[0],
        Number = s.Number ?? "",
        Total = OwnDocuments.Total(s),
        AmountDue = s.Document.Kind == DocumentKind.Invoice ? OwnDocuments.Due(s) : null,
        DueOrExpiryDate = s.Document.DueOrExpiryDate,
        Reference = s.Document.Reference,
        OnlineLink = payLink,
        TradingName = string.IsNullOrWhiteSpace(brand.CompanyName) ? "Webignite" : brand.CompanyName.Trim(),
    };
}
