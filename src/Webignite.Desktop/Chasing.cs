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
        if (services.Store.ListMailAccounts().FirstOrDefault(a => a.SendsInvoices) is not { } mailbox)
            return ("No mailbox is set up to send invoices. In Mail, Manage mailboxes, tick \"Invoices are sent from this mailbox\" on accounts@.", true);
        try
        {
            var (ready, settled) = await sending.PrepareChaserAsync(stored, services.XeroSettings.TenantName ?? "Webignite");
            if (ready is null)
                return ($"Xero says {stored.XeroNumber ?? "it"} is {settled}, so there's nothing to chase.", false);

            var folder = Path.Combine(DataPaths.Root(), "outgoing");
            Directory.CreateDirectory(folder);
            var pdf = Path.Combine(folder, string.Join("_", ready.PdfName.Split(Path.GetInvalidFileNameChars())));
            await File.WriteAllBytesAsync(pdf, ready.Pdf);

            var filled = DocumentEmail.Fill(services.EmailTemplates.ChaserFor(stored.Document.Kind), ready.Values);
            var to = Recipients.Parse(ready.ContactEmail);
            var mail = new OutgoingMail
            {
                AccountId = mailbox.Id,
                To = to.People,
                Subject = filled.Subject,
                Body = filled.Body + Replies.StartBody(mailbox.Signature),
                Attachments = [new OutgoingAttachment(ready.PdfName, ready.Pdf.Length, FilePath: pdf)],
            };
            var notes = new List<string>();
            if (to.People.Count == 0)
                notes.Add($"Xero has no email address for {ready.Values.ContactName}. Type it in To.");
            if (filled.Unfilled.Count > 0)
                notes.Add($"Couldn't fill {string.Join(", ", filled.Unfilled)}. Fix the text before sending.");

            var opened = services.ComposeMail?.Invoke(mail, notes.Count > 0 ? string.Join(" ", notes) : null, () =>
            {
                sending.MarkChased(services.Store.Get(stored.Id) ?? ready.Stored);
                return Task.CompletedTask;
            }) ?? false;
            return opened ? (null, false) : ("Another mail is open in Mail with changes. Send or close it first, then try again.", true);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            return ("Couldn't get it ready: " + e.Message, true);
        }
    }
}
