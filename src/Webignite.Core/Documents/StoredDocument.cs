namespace Webignite.Core.Documents;

public enum DocumentStatus
{
    /// <summary>Being written. Nothing has gone anywhere.</summary>
    Draft,

    /// <summary>Prepared by Claude or ChatGPT and waiting for a person to approve it.</summary>
    AwaitingApproval,

    /// <summary>Created in Xero as a draft.</summary>
    InXero,
}

/// <summary>
/// How Xero settled a quote or invoice, so there's nothing left to chase. Saved as lower-case text ("paid"),
/// the same as before it was typed.
/// </summary>
public enum Settlement
{
    Paid,
    Voided,
    Deleted,
    Accepted,
    Declined,
    Invoiced,
}

public static class Settlements
{
    /// <summary>Settled well: paid, or a quote accepted or turned into an invoice. Shown in green.</summary>
    public static bool IsGood(Settlement? s) => s is Settlement.Paid or Settlement.Accepted or Settlement.Invoiced;

    /// <summary>Gone in Xero: voided or deleted, so it no longer counts for anything.</summary>
    public static bool IsCancelled(Settlement? s) => s is Settlement.Voided or Settlement.Deleted;

    /// <summary>The word for it: "Paid", "Accepted".</summary>
    public static string Label(Settlement s) => s.ToString();

    /// <summary>The same in a sentence: "paid", "accepted".</summary>
    public static string Word(Settlement s) => s.ToString().ToLowerInvariant();
}

/// <summary>The statuses Xero gives quotes and invoices, as Xero spells them.</summary>
public static class XeroStatuses
{
    public const string Draft = "DRAFT";
    public const string Submitted = "SUBMITTED";
    public const string Authorised = "AUTHORISED";
    public const string Sent = "SENT";
    public const string Paid = "PAID";
    public const string Voided = "VOIDED";
    public const string Deleted = "DELETED";
    public const string Accepted = "ACCEPTED";
    public const string Declined = "DECLINED";
    public const string Invoiced = "INVOICED";

    /// <summary>Not yet approved in Xero, so the customer hasn't got it.</summary>
    public static bool IsDraft(string? status) => status is Draft or Submitted;
}

/// <summary>Who wrote the document.</summary>
public enum DocumentSource
{
    App,
    Claude,
    ChatGpt,
}

/// <summary>A document as kept on this PC, with what we know about it in Xero.</summary>
public sealed record StoredDocument
{
    public required string Id { get; init; }
    public required Document Document { get; init; }
    public DocumentStatus Status { get; init; } = DocumentStatus.Draft;
    public DocumentSource Source { get; init; } = DocumentSource.App;

    /// <summary>Its copy in Xero and what Xero last said about it.</summary>
    public XeroLink Xero { get; init; } = new();

    /// <summary>Approving, emailing and chasing it.</summary>
    public Delivery Delivery { get; init; } = new();

    /// <summary>Set when Xero says there's nothing left to chase: the invoice is paid, or the quote accepted or declined.</summary>
    public SettledState? Settled { get; init; }

    /// <summary>Files attached to its copy in Xero from here.</summary>
    public IReadOnlyList<XeroAttachment> Attachments { get; init; } = [];

    /// <summary>Credit notes drafted in Xero against this invoice from here.</summary>
    public IReadOnlyList<CreditNoteLink> CreditNotes { get; init; } = [];

    /// <summary>While a draft in Xero is being changed here: the document as it was, for Discard changes.</summary>
    public Document? BeforeChange { get; init; }

    /// <summary>For an invoice made from a quote here: that quote's id, so each can show the other.</summary>
    public string? FromQuote { get; init; }

    /// <summary>When it was taken off To approve by hand. It stays in its list, and nothing changes in Xero.</summary>
    public DateTimeOffset? OffApprovals { get; init; }

    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Updated { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>A file attached to a quote or invoice in Xero.</summary>
public sealed record XeroAttachment
{
    public required string Name { get; init; }
    public long Bytes { get; init; }

    /// <summary>Shown to the client on the online invoice.</summary>
    public bool Online { get; init; }
    public DateTimeOffset Added { get; init; }
}

/// <summary>A credit note drafted in Xero against an invoice. Approving and allocating it is done in Xero.</summary>
public sealed record CreditNoteLink
{
    public required string Id { get; init; }
    public string? Number { get; init; }
    public decimal Amount { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset Drafted { get; init; }
}

/// <summary>A document's copy in Xero: how the write went, and what Xero last said.</summary>
public sealed record XeroLink
{
    public string? Id { get; init; }
    public string? Number { get; init; }

    /// <summary>Where the Xero write stands, and the Xero contact it used.</summary>
    public Webignite.Core.Xero.XeroWriteState State { get; init; }
    public string? ContactId { get; init; }

    /// <summary>Exactly what was sent to Xero, kept so it can be checked against what Xero saved.</summary>
    public string? Sent { get; init; }

    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>What Xero last said: its status (AUTHORISED, PAID, ACCEPTED...), what's still due and what's been paid.</summary>
    public string? Status { get; init; }
    public decimal? AmountDue { get; init; }
    public decimal? AmountPaid { get; init; }

    /// <summary>The day Xero says the invoice was paid in full.</summary>
    public DateOnly? PaidOn { get; init; }

    /// <summary>When Xero was last read for this document's status and payments.</summary>
    public DateTimeOffset? Checked { get; init; }
}

/// <summary>Getting a document to the customer and following it up.</summary>
public sealed record Delivery
{
    /// <summary>When Eddie approved it in Xero from here (invoices become AUTHORISED).</summary>
    public DateTimeOffset? Approved { get; init; }

    /// <summary>When it was last emailed to the customer from Webignite's own mailbox.</summary>
    public DateTimeOffset? Emailed { get; init; }

    /// <summary>Who it was last emailed to, as shown: "Thandi Mokoena" or "Thandi Mokoena and Pieter".</summary>
    public string? EmailedTo { get; init; }

    /// <summary>When a reminder (invoice) or follow-up (quote) last went out, and how many so far.</summary>
    public DateTimeOffset? Chased { get; init; }
    public int ChaseCount { get; init; }
}

/// <summary>When a document was settled and how. <see cref="As"/> is missing on a few settled before the reason was kept.</summary>
public sealed record SettledState
{
    public required DateTimeOffset On { get; init; }
    public Settlement? As { get; init; }
}
