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
    public string? XeroId { get; init; }
    public string? XeroNumber { get; init; }

    /// <summary>Where the Xero write stands, and the Xero contact it used.</summary>
    public Xero.XeroWriteState XeroState { get; init; }
    public string? XeroContactId { get; init; }

    /// <summary>Exactly what was sent to Xero, kept so it can be checked against what Xero saved.</summary>
    public string? XeroSent { get; init; }

    public IReadOnlyList<string> XeroProblems { get; init; } = [];

    /// <summary>When Eddie approved it in Xero from here (invoices become AUTHORISED).</summary>
    public DateTimeOffset? Approved { get; init; }

    /// <summary>When it was last emailed to the customer from Webignite's own mailbox.</summary>
    public DateTimeOffset? Emailed { get; init; }

    /// <summary>Who it was last emailed to, as shown: "Thandi Mokoena" or "Thandi Mokoena and Pieter".</summary>
    public string? EmailedTo { get; init; }

    /// <summary>When a reminder (invoice) or follow-up (quote) last went out, and how many so far.</summary>
    public DateTimeOffset? Chased { get; init; }
    public int ChaseCount { get; init; }

    /// <summary>When Xero said there's nothing left to chase: the invoice is paid, or the quote accepted or declined.</summary>
    public DateTimeOffset? Settled { get; init; }
    public Settlement? SettledAs { get; init; }

    /// <summary>What Xero last said: its status (AUTHORISED, PAID, ACCEPTED...), what's still due and what's been paid.</summary>
    public string? XeroStatus { get; init; }
    public decimal? XeroAmountDue { get; init; }
    public decimal? XeroAmountPaid { get; init; }

    /// <summary>The day Xero says the invoice was paid in full.</summary>
    public DateOnly? PaidOn { get; init; }

    /// <summary>When Xero was last read for this document's status and payments.</summary>
    public DateTimeOffset? XeroChecked { get; init; }

    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Updated { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}
