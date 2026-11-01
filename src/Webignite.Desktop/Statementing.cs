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
        if (services.Store.ListMailAccounts().FirstOrDefault(a => a.SendsInvoices) is not { } mailbox)
            return ("No mailbox is set up to send invoices. In Mail, Manage mailboxes, tick \"Invoices are sent from this mailbox\" on accounts@.", true);
        var today = DateOnly.FromDateTime(DateTime.Today);
        try
        {
            var contactId = Statements.ContactIdFor(customer, services.Store.List(), services.Store.ListClients());
            var ready = await sending.PrepareStatementAsync(customer, contactId);
            if (ready.Lines.Count == 0)
            {
                services.StatementsSent = Statements.MarkSent(services.StatementsSent, customer, today);
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
                TradingName = services.XeroSettings.TenantName ?? "Webignite",
            };
            var filled = DocumentEmail.Fill(services.EmailTemplates.Statement, values);
            var to = Recipients.Parse(ready.Email);
            var mail = new OutgoingMail
            {
                AccountId = mailbox.Id,
                To = to.People,
                Subject = filled.Subject,
                Body = filled.Body + Replies.StartBody(mailbox.Signature),
            };
            var notes = new List<string>();
            if (to.People.Count == 0)
                notes.Add($"Xero has no email address for {ready.ContactName}. Type it in To.");
            if (filled.Unfilled.Count > 0)
                notes.Add($"Couldn't fill {string.Join(", ", filled.Unfilled)}. Fix the text before sending.");

            var opened = services.ComposeMail?.Invoke(mail, notes.Count > 0 ? string.Join(" ", notes) : null, () =>
            {
                services.StatementsSent = Statements.MarkSent(services.StatementsSent, customer, DateOnly.FromDateTime(DateTime.Today));
                return Task.CompletedTask;
            }) ?? false;
            return opened ? (null, false) : ("Another mail is open in Mail with changes. Send or close it first, then try again.", true);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ("Couldn't get it ready: " + e.Message, true);
        }
    }
}
