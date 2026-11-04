using Webignite.Core.Mail;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>A mail prepared for the person to read, change and send. Nothing is sent from here.</summary>
public sealed record MailDraft(MailAccount From, string? To, string Subject, string Body)
{
    public IReadOnlyList<OutgoingAttachment> Attachments { get; init; } = [];

    /// <summary>What to say when there's nobody to send it to, e.g. "Xero has no email address for Karoo. Type it in To."</summary>
    public string NoAddress { get; init; } = "Type who it goes to in To.";

    /// <summary>Placeholders the template left unfilled, named in the note.</summary>
    public IReadOnlyList<string> Unfilled { get; init; } = [];

    /// <summary>Anything else worth saying above the mail.</summary>
    public string? Note { get; init; }

    /// <summary>Runs once the mail has gone, given who it went to, e.g. to tell Xero.</summary>
    public Func<IReadOnlyList<MailPerson>, Task>? AfterSent { get; init; }
}

/// <summary>
/// The one way a mail is prepared and opened in Mail: which mailbox it goes from, attachments saved where Mail
/// can read them, the signature added, and a note about anything still missing. Used for invoices and quotes,
/// reminders, statements, onboarding reminders and mark-ups.
/// </summary>
public sealed class MailDrafts(AppServices services)
{
    public const string NoInvoiceMailbox = "No mailbox is set up to send invoices. In Mail, Manage mailboxes, tick \"Invoices are sent from this mailbox\" on accounts@.";
    public const string NoMailbox = "Add a mailbox under Mail first.";
    public const string AnotherOpen = "Another mail is open in Mail with changes. Send or close it first, then try again.";

    /// <summary>The mailbox invoices, quotes, reminders and statements go from.</summary>
    public MailAccount? InvoiceMailbox => MailAccounts.ForInvoices(services.Store.ListMailAccounts());

    /// <summary>The mailbox everyday mail goes from.</summary>
    public MailAccount? EverydayMailbox => MailAccounts.ForEveryday(services.Store.ListMailAccounts());

    /// <summary>Saves a file to attach in the outgoing folder, with a name Windows accepts.</summary>
    public async Task<OutgoingAttachment> AttachAsync(string fileName, byte[] data)
    {
        var folder = Path.Combine(DataPaths.Root(), "outgoing");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Join("_", fileName.Split(Path.GetInvalidFileNameChars())));
        await File.WriteAllBytesAsync(path, data);
        return new OutgoingAttachment(fileName, data.Length, FilePath: path);
    }

    /// <summary>Opens the draft in Mail. False when another mail with changes is still open there.</summary>
    public bool Open(MailDraft draft)
    {
        var to = Recipients.Parse(draft.To);
        var mail = new OutgoingMail
        {
            AccountId = draft.From.Id,
            To = to.People,
            Subject = draft.Subject,
            Body = draft.Body + Replies.StartBody(draft.From.Signature),
            Attachments = draft.Attachments,
        };
        return services.Go.ComposeMail(mail, DraftNotes.For(to.People.Count, draft.NoAddress, draft.Unfilled, draft.Note), draft.AfterSent);
    }
}
