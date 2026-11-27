using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// Quotes and invoices made final in Webignite itself, once that's switched on in Settings: Approve and email numbers
// it (carrying on from the numbers there already are) and opens the mail with Webignite's own PDF; payments are
// recorded here. Xero isn't written to for these.
public partial class DocumentEditorViewModel
{
    /// <summary>New quotes and invoices are made final here, not in Xero (Settings, Quotes and invoices).</summary>
    public bool OwnMode => documentSettings.MakeInWebignite;

    /// <summary>Made final here: numbered, and no longer changed.</summary>
    public bool IsIssued => stored?.Status == DocumentStatus.Issued;

    /// <summary>Approve and email (or Email the quote) shows for a draft while quotes and invoices are made here.</summary>
    public bool ShowIssue => OwnMode && !IsIssued && XeroState == Webignite.Core.Xero.XeroWriteState.None;

    public string IssueButtonText => Kind == DocumentKind.Quote ? "Email the quote" : "Approve and email";

    public string IssueHint => !IsReady ? "Finish the document first."
        : Kind == DocumentKind.Quote
            ? $"It gets the next quote number ({NextNumberShown}), then the mail opens with the PDF attached. Nothing is sent until you press Send."
            : $"It gets the next invoice number ({NextNumberShown}) and counts as owed, then the mail opens with the PDF attached. Nothing is sent until you press Send.";

    private string NextNumberShown => OwnInvoicing.NextNumber(Kind, services.Documents.List(Kind).Where(d => d.Id != stored?.Id), services.Settings.Documents.NextNumberFor(Kind));

    public bool CanIssue => ShowIssue && IsReady && !XeroBusy;

    public string NumberPlaceholder => OwnMode ? "Next number" : "Xero's next";

    [ObservableProperty]
    public partial string PaymentNote { get; set; } = "";

    /// <summary>Makes it final here and opens the mail with Webignite's own PDF.</summary>
    [RelayCommand]
    private Task Issue() => IssueThen(email: true);

    /// <summary>Makes it final here without mailing it, e.g. when it went another way.</summary>
    [RelayCommand]
    private Task IssueOnly() => IssueThen(email: false);

    private Task IssueThen(bool email)
    {
        if (!CanIssue)
            return Task.CompletedTask;
        if (ClientToCheck() is { } check)
        {
            ShowXero(check, problem: true);
            return Task.CompletedTask;
        }
        Save();
        if (stored is null)
            return Task.CompletedTask;
        var all = services.Documents.List(Kind);
        var issued = services.Documents.Change(stored.Id, s => OwnInvoicing.Issue(s, all, DateTimeOffset.Now, services.Settings.Documents.NextNumberFor(Kind)));
        if (issued is not { Status: DocumentStatus.Issued })
        {
            ShowXero("It couldn't be made final: it may have gone to Xero from another page meanwhile.", problem: true);
            return Task.CompletedTask;
        }
        stored = issued;
        NumberText = issued.Own!.Number;
        changed = false;
        AfterOwnChange();
        if (!email)
        {
            ShowXero($"{issued.Own.Number} is made final. Email it when you're ready.", problem: false);
            return Task.CompletedTask;
        }
        return PrepareOwnEmail();
    }

    /// <summary>The mail with Webignite's own PDF of it, from the invoice mailbox. It goes only when Send is pressed.</summary>
    private async Task PrepareOwnEmail()
    {
        if (stored is null)
            return;
        stored = services.Documents.Get(stored.Id) ?? stored;
        if (services.Drafts.InvoiceMailbox is not { } mailbox)
        {
            ShowXero(MailDrafts.NoInvoiceMailbox, problem: true);
            return;
        }
        XeroBusy = true;
        try
        {
            var client = services.Clients.Find(stored.Document.Customer.Name);
            var brand = services.Settings.MailBrand;
            var sheet = DocumentSheets.Build(stored, brand, client);
            var bytes = await Task.Run(() => Webignite.Tools.DocumentPdfMaker.Make(sheet));
            var pdf = await services.Drafts.AttachAsync(sheet.FileName, bytes);
            var values = new DocumentEmailValues
            {
                Kind = Kind,
                ContactName = client?.Called ?? stored.Document.Customer.Name,
                FirstName = FirstName(client?.ContactPerson),
                Number = OwnInvoicing.NumberOf(stored) ?? "",
                Total = stored.Document.Total ?? 0,
                AmountDue = Kind == DocumentKind.Invoice ? OwnInvoicing.Due(stored) : null,
                DueOrExpiryDate = stored.Document.DueOrExpiryDate,
                Reference = stored.Document.Reference,
                OnlineLink = sheet.PayLink,
                TradingName = string.IsNullOrWhiteSpace(brand.CompanyName) ? "Webignite" : brand.CompanyName.Trim(),
            };
            var filled = DocumentEmail.Fill(services.Settings.EmailTemplates.For(Kind), values);
            var id = stored.Id;
            var opened = services.Drafts.Open(new MailDraft(mailbox,
                services.Contacts.AddressFor(stored.Document.Customer.Name, Kind == DocumentKind.Quote ? MailFor.Quotes : MailFor.Invoices) ?? ClientMail.First(client?.Email) ?? stored.Document.Customer.Email,
                filled.Subject, filled.Body)
            {
                Attachments = [pdf],
                NoAddress = $"No email address is saved for {values.ContactName}. Type it in To.",
                Unfilled = filled.Unfilled,
                AfterSent = sentTo =>
                {
                    var now = DateTimeOffset.Now;
                    stored = services.Documents.Change(id, s => s with
                    {
                        Delivery = s.Delivery with
                        {
                            Approved = s.Delivery.Approved ?? now,
                            Emailed = now,
                            EmailedTo = sentTo is { Count: > 0 } ? Recipients.Short(sentTo) : s.Delivery.EmailedTo,
                        },
                    }) ?? stored;
                    AfterOwnChange();
                    return Task.CompletedTask;
                },
                Note = BrandedNote,
                Layout = () => DocumentMailLayout.Build(values, brand, AcceptTo(mailbox)),
            });
            ShowXero(opened ? "" : MailDrafts.AnotherOpen, problem: !opened);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Webignite.Data.AppLog.Write("Getting a quote or invoice made here ready to email", e);
            ShowXero("Couldn't get it ready: " + e.Message, problem: true);
        }
        finally
        {
            XeroBusy = false;
            AfterOwnChange();
        }
    }

    private static string? FirstName(string? person) =>
        string.IsNullOrWhiteSpace(person) ? null : person.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private void RecordOwnPayment(decimal? amount, DateOnly? day)
    {
        if (stored is null)
            return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (OwnInvoicing.Check(stored, amount, day, today) is { } problem)
        {
            ShowXero(problem, problem: true);
            return;
        }
        var note = PaymentNote;
        var recorded = services.Documents.Change(stored.Id, s => OwnInvoicing.Record(s, amount!.Value, day!.Value, note, DateTimeOffset.Now));
        if (recorded is null || OwnInvoicing.Paid(recorded) == OwnInvoicing.Paid(stored))
        {
            ShowXero("It couldn't be recorded: it may have been paid off from another page meanwhile.", problem: true);
            return;
        }
        stored = recorded;
        RecordingPayment = false;
        // It shows under Money received too, so the month's figures follow without typing it again.
        services.Store.SaveEntry(new Webignite.Core.Finance.MoneyEntry
        {
            Id = Webignite.Core.Finance.MoneyEntry.NewId(),
            Kind = Webignite.Core.Finance.EntryKind.Received,
            Date = day!.Value,
            Amount = Money.Cents(amount!.Value),
            Party = recorded.Document.Customer.Name,
            Note = recorded.Own!.Number + (string.IsNullOrWhiteSpace(note) ? "" : ", " + note.Trim()),
            DocumentId = recorded.Id,
            Created = DateTimeOffset.Now,
        });
        ShowXero(recorded.Settled?.As == Settlement.Paid
            ? $"{Money.Rand(amount!.Value)} recorded. {recorded.Own!.Number} is paid."
            : $"{Money.Rand(amount!.Value)} recorded. {Money.Rand(OwnInvoicing.Due(recorded))} is still owed.", problem: false);
        AfterOwnChange();
    }

    // A change to one made here, applied to it as stored now; says what happened, or that it had moved on meanwhile.
    private void OwnChange(Func<StoredDocument, StoredDocument?> change, Func<StoredDocument, string> said)
    {
        if (stored is null)
            return;
        StoredDocument? made = null;
        var saved = services.Documents.Change(stored.Id, s => made = change(s));
        if (made is null || saved is null)
        {
            ShowXero("It couldn't be done: it changed on another page meanwhile.", problem: true);
            AfterOwnChange();
            return;
        }
        stored = saved;
        ShowXero(said(saved), problem: false);
        AfterOwnChange();
    }

    private void AfterOwnChange()
    {
        RefreshXero();
        RefreshEmail();
        foreach (var name in new[] { nameof(IsIssued), nameof(ShowIssue), nameof(CanIssue), nameof(IssueHint), nameof(Title), nameof(NumberNote),
            nameof(CanRecordPayment), nameof(PaymentHint), nameof(PaymentTitle), nameof(IsEditable), nameof(CanType), nameof(CanPickDates), nameof(ShowCreate),
            nameof(CanVoid), nameof(VoidLabel), nameof(VoidQuestion), nameof(CanCredit), nameof(CreditHint), nameof(CreditButtonText), nameof(CreditTitle), nameof(CreditStartText), nameof(CreditNotesText),
            nameof(HasCreditNotes), nameof(CanDecideQuote) })
            OnPropertyChanged(name);
    }
}
