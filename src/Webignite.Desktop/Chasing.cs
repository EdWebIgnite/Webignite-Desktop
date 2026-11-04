using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Data;
using Webignite.Xero;

namespace Webignite.Desktop;

/// <summary>Drafts a reminder for an overdue invoice, or a follow-up for a quote, from the invoices mailbox.</summary>
public static class Chasing
{
    /// <summary>
    /// Checks Xero, then opens the drafted mail in Mail for Eddie to read and send. Returns what to tell Eddie:
    /// a problem, that Xero says it's settled, or null when the mail is open.
    /// </summary>
    public static async Task<(string? Message, bool Problem)> DraftAsync(AppServices services, StoredDocument stored)
    {
        if (services.XeroSending is not { } sending)
            return ("Connect Xero in Settings first.", true);
        if (services.Drafts.InvoiceMailbox is not { } mailbox)
            return (MailDrafts.NoInvoiceMailbox, true);
        try
        {
            var (ready, settled) = await sending.PrepareChaserAsync(stored, services.Settings.Xero.TenantName ?? "Webignite");
            if (ready is null)
                return ($"Xero says {stored.XeroNumber ?? "it"} is {(settled is { } how ? Settlements.Word(how) : "settled")}, so there's nothing to chase.", false);

            var pdf = await services.Drafts.AttachAsync(ready.PdfName, ready.Pdf);
            var filled = DocumentEmail.Fill(services.Settings.EmailTemplates.ChaserFor(stored.Document.Kind), ready.Values);
            var opened = services.Drafts.Open(new MailDraft(mailbox, ready.ContactEmail, filled.Subject, filled.Body)
            {
                Attachments = [pdf],
                NoAddress = $"Xero has no email address for {ready.Values.ContactName}. Type it in To.",
                Unfilled = filled.Unfilled,
                AfterSent = _ =>
                {
                    sending.MarkChased(services.Documents.Get(stored.Id) ?? ready.Stored);
                    services.Documents.TickOffChasing(stored.Id, DateTimeOffset.Now);
                    return Task.CompletedTask;
                },
            });
            return opened ? (null, false) : (MailDrafts.AnotherOpen, true);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            return ("Couldn't get it ready: " + e.Message, true);
        }
    }
}
