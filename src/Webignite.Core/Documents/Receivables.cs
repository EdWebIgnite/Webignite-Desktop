namespace Webignite.Core.Documents;

/// <summary>A quote or invoice as Xero has it now: only what matters for money owed and chasing.</summary>
public sealed record XeroStanding(string Status, decimal? AmountDue, decimal? AmountPaid, DateOnly? PaidOn);

/// <summary>A payment Xero recorded since the last look, to tell Eddie about.</summary>
public sealed record PaymentNews(StoredDocument Stored, decimal Amount, bool PaidInFull)
{
    public string Title => PaidInFull
        ? $"{Stored.Document.Customer.Name} paid {Stored.XeroNumber ?? "an invoice"}"
        : $"Part payment from {Stored.Document.Customer.Name}";

    public string Text => PaidInFull
        ? $"{Money.Rand(Amount)} came in. Nothing more is owed on it."
        : $"{Money.Rand(Amount)} came in for {Stored.XeroNumber ?? "an invoice"}. {Money.Rand(Stored.XeroAmountDue ?? 0)} is still owed.";
}

/// <summary>
/// What's owed, using what Xero says about each invoice. Xero is only read, never changed. Until an
/// invoice has been read from Xero, its full total counts as owed.
/// </summary>
public static class Receivables
{
    /// <summary>Invoices and quotes worth reading from Xero: in Xero and not settled yet.</summary>
    public static bool NeedsChecking(StoredDocument d) =>
        d.Status == DocumentStatus.InXero && d.XeroId is not null && d.Settled is null;

    /// <summary>An invoice the customer has and hasn't settled. Drafts still waiting for approval in Xero don't count.</summary>
    public static bool IsOpenInvoice(StoredDocument d) =>
        d.Document.Kind == DocumentKind.Invoice && d.Status == DocumentStatus.InXero && d.Settled is null
        && d.XeroStatus is not ("DRAFT" or "SUBMITTED");

    /// <summary>What's still owed on an invoice: Xero's amount due when known, otherwise the total.</summary>
    public static decimal Outstanding(StoredDocument d) =>
        IsOpenInvoice(d) ? Math.Max(0, d.XeroAmountDue ?? d.Document.Total ?? 0) : 0;

    public static bool IsOverdue(StoredDocument d, DateOnly today) =>
        Outstanding(d) > 0 && d.Document.DueOrExpiryDate is { } due && due < today;

    /// <summary>A quote still waiting on the client: not accepted, declined or expired.</summary>
    public static bool IsOpenQuote(StoredDocument d, DateOnly today) =>
        d.Document.Kind == DocumentKind.Quote && d.Status == DocumentStatus.InXero && d.Settled is null
        && !(d.Document.DueOrExpiryDate < today);

    public static decimal Owed(IEnumerable<StoredDocument> documents) => documents.Sum(Outstanding);

    public static decimal Overdue(IEnumerable<StoredDocument> documents, DateOnly today) =>
        documents.Where(d => IsOverdue(d, today)).Sum(Outstanding);

    public static decimal QuotesOut(IEnumerable<StoredDocument> documents, DateOnly today) =>
        documents.Where(d => IsOpenQuote(d, today)).Sum(d => d.Document.Total ?? 0);

    /// <summary>
    /// Takes in what Xero says. Paid, voided, accepted or declined documents are settled, so they stop
    /// counting and stop being chased. A payment is news only once Xero has been read before, so the first
    /// look doesn't announce every old payment. Returns null for the document when nothing changed.
    /// </summary>
    public static (StoredDocument? Changed, PaymentNews? News) Apply(StoredDocument stored, XeroStanding xero, DateTimeOffset now)
    {
        // A draft invoice says nothing about payment, even when its total is nil.
        var settledAs = xero.Status is "DRAFT" or "SUBMITTED" ? null : Chasers.SettledBy(stored.Document.Kind, xero.Status, xero.AmountDue);

        var updated = stored with
        {
            XeroStatus = xero.Status,
            XeroAmountDue = xero.AmountDue,
            XeroAmountPaid = xero.AmountPaid,
            PaidOn = xero.PaidOn ?? stored.PaidOn,
            Settled = settledAs is null ? stored.Settled : stored.Settled ?? now,
            SettledAs = settledAs ?? stored.SettledAs,
        };

        PaymentNews? news = null;
        var paidBefore = stored.XeroAmountPaid ?? 0;
        if (stored.Document.Kind == DocumentKind.Invoice && stored.XeroChecked is not null
            && xero.AmountPaid is { } paid && Money.Cents(paid) > Money.Cents(paidBefore))
            news = new PaymentNews(updated, paid - paidBefore, settledAs == "paid");

        var same = updated with { XeroChecked = stored.XeroChecked } == stored;
        if (same && stored.XeroChecked is not null)
            return (null, null);
        return (updated with { XeroChecked = now }, news);
    }

    /// <summary>How the invoice or quote stands, for the lists: "Paid", "Part paid", "Accepted", or null when Xero hasn't said.</summary>
    public static string? Standing(StoredDocument d) => d.SettledAs switch
    {
        "paid" => "Paid",
        "voided" => "Voided",
        "deleted" => "Deleted in Xero",
        "accepted" => "Accepted",
        "declined" => "Declined",
        "invoiced" => "Invoiced",
        _ when d.Document.Kind == DocumentKind.Invoice && d.XeroAmountPaid > 0 && d.XeroAmountDue > 0 => "Part paid",
        _ when d.XeroStatus is "DRAFT" or "SUBMITTED" => "Draft in Xero",
        _ when d.XeroStatus == "AUTHORISED" => "Approved",
        _ when d.XeroStatus == "SENT" => "Sent",
        _ => null,
    };
}
