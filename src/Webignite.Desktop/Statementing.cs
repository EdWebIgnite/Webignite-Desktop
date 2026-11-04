using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Xero;

namespace Webignite.Desktop;

/// <summary>Drafts a monthly statement for one customer from the invoices mailbox, built from Xero.</summary>
public static class Statementing
{
    /// <summary>
    /// Reads the customer's unpaid invoices from Xero and opens the statement in Mail for Eddie to read and
    /// send. Returns what to tell Eddie: a problem, that nothing is owed, or null when the mail is open.
    /// </summary>
    public static async Task<(string? Message, bool Problem)> DraftAsync(AppServices services, string customer)
    {
        if (services.XeroSending is not { } sending)
            return ("Connect Xero in Settings first.", true);
        if (services.Drafts.InvoiceMailbox is not { } mailbox)
            return (MailDrafts.NoInvoiceMailbox, true);
        var today = DateOnly.FromDateTime(DateTime.Today);
        try
        {
            var contactId = Statements.ContactIdFor(customer, services.Store.List(), services.Store.ListClients());
            var ready = await sending.PrepareStatementAsync(customer, contactId);
            if (ready.Lines.Count == 0)
            {
                services.Settings.StatementsSent = Statements.MarkSent(services.Settings.StatementsSent, customer, today);
                return ($"Xero says {ready.ContactName} owes nothing, so there's no statement to send.", false);
            }

            var values = new DocumentEmailValues
            {
                Kind = DocumentKind.Invoice,
                ContactName = ready.ContactName,
                FirstName = ready.FirstName,
                Number = "",
                Total = Statements.TotalDue(ready.Lines),
                AmountDue = Statements.TotalDue(ready.Lines),
                StatementLines = Statements.Lines(ready.Lines, today),
                StatementDate = today,
                TradingName = services.Settings.Xero.TenantName ?? "Webignite",
            };
            var filled = DocumentEmail.Fill(services.Settings.EmailTemplates.Statement, values);
            var opened = services.Drafts.Open(new MailDraft(mailbox, ready.Email, filled.Subject, filled.Body)
            {
                NoAddress = $"Xero has no email address for {ready.ContactName}. Type it in To.",
                Unfilled = filled.Unfilled,
                AfterSent = _ =>
                {
                    services.Settings.StatementsSent = Statements.MarkSent(services.Settings.StatementsSent, customer, DateOnly.FromDateTime(DateTime.Today));
                    return Task.CompletedTask;
                },
            });
            return opened ? (null, false) : (MailDrafts.AnotherOpen, true);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ("Couldn't get it ready: " + e.Message, true);
        }
    }
}
