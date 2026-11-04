namespace Webignite.Core.Documents;

/// <summary>A quote or invoice as Xero has it now: only what matters for money owed and chasing.</summary>
public sealed record XeroStanding(string Status, decimal? AmountDue, decimal? AmountPaid, DateOnly? PaidOn);

/// <summary>A payment Xero recorded since the last look, to tell Eddie about.</summary>
public sealed record PaymentNews(StoredDocument Stored, decimal Amount, bool PaidInFull)
{
    public string Title => PaidInFull
        ? $"{Stored.Document.Customer.Name} paid {Stored.Xero.Number ?? "an invoice"}"
        : $"Part payment from {Stored.Document.Customer.Name}";

    public string Text => PaidInFull
        ? $"{Money.Rand(Amount)} came in. Nothing more is owed on it."
        : $"{Money.Rand(Amount)} came in for {Stored.Xero.Number ?? "an invoice"}. {Money.Rand(Stored.Xero.AmountDue ?? 0)} is still owed.";
}

/// <summary>
/// What's owed, using what Xero says about each invoice. Xero is only read, never changed. Until an
/// invoice has been read from Xero, its full total counts as owed.
/// </summary>
public static class Receivables
{
    /// <summary>Invoices and quotes worth reading from Xero: in Xero and not settled yet.</summary>
    public static bool NeedsChecking(StoredDocument d) =>
        d.Status == DocumentStatus.InXero && d.Xero.Id is not null && d.Settled is null;

    /// <summary>An invoice the customer has and hasn't settled. Drafts still waiting for approval in Xero don't count.</summary>
    public static bool IsOpenInvoice(StoredDocument d) =>
        d.Document.Kind == DocumentKind.Invoice && d.Status == DocumentStatus.InXero && d.Settled is null
        && !XeroStatuses.IsDraft(d.Xero.Status);

    /// <summary>What's still owed on an invoice: Xero's amount due when known, otherwise the total.</summary>
    public static decimal Outstanding(StoredDocument d) =>
        IsOpenInvoice(d) ? Math.Max(0, d.Xero.AmountDue ?? d.Document.Total ?? 0) : 0;

    public static bool IsOverdue(StoredDocument d, DateOnly today) =>
        Outstanding(d) > 0 && d.Document.DueOrExpiryDate is { } due && due < today;

    /// <summary>
    /// A quote waiting on the client: the client has it (emailed from here, or marked sent in Xero) and it isn't
    /// accepted, declined or expired. A draft the client never saw isn't waiting on anyone, as with invoices.
    /// </summary>
    public static bool IsOpenQuote(StoredDocument d, DateOnly today) =>
        d.Document.Kind == DocumentKind.Quote && d.Status == DocumentStatus.InXero && d.Settled is null
        && (d.Delivery.Emailed is not null || d.Xero.Status == XeroStatuses.Sent)
        && !(d.Document.DueOrExpiryDate < today);

    /// <summary>The word on a quote or invoice row: Draft, To approve, Overdue, Paid, Accepted and so on.</summary>
    public static string StatusText(StoredDocument d, DateOnly today) => d.Status switch
    {
        DocumentStatus.Draft => "Draft",
        DocumentStatus.AwaitingApproval => "To approve",
        DocumentStatus.InXero when IsOverdue(d, today) => "Overdue",
        DocumentStatus.InXero => Standing(d) ?? "In Xero",
        _ => d.Status.ToString(),
    };

    /// <summary>
    /// Shown in green: paid, accepted or invoiced, or in Xero before Xero has been read for payments (and not
    /// overdue).
    /// </summary>
    public static bool IsDone(StoredDocument d, DateOnly today) => Settlements.IsGood(d.Settled?.As)
        || (d.Status == DocumentStatus.InXero && d.Xero.Checked is null && !IsOverdue(d, today));

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
        var settledAs = XeroStatuses.IsDraft(xero.Status) ? null : Chasers.SettledBy(stored.Document.Kind, xero.Status, xero.AmountDue);

        var updated = stored with
        {
            Xero = stored.Xero with
            {
                Status = xero.Status,
                AmountDue = xero.AmountDue,
                AmountPaid = xero.AmountPaid,
                PaidOn = xero.PaidOn ?? stored.Xero.PaidOn,
            },
            Settled = settledAs is null ? stored.Settled
                : stored.Settled is { } settled ? settled with { As = settledAs } : new SettledState { On = now, As = settledAs },
        };

        PaymentNews? news = null;
        var paidBefore = stored.Xero.AmountPaid ?? 0;
        if (stored.Document.Kind == DocumentKind.Invoice && stored.Xero.Checked is not null
            && xero.AmountPaid is { } paid && Money.Cents(paid) > Money.Cents(paidBefore))
            news = new PaymentNews(updated, paid - paidBefore, settledAs == Settlement.Paid);

        var same = updated with { Xero = updated.Xero with { Checked = stored.Xero.Checked } } == stored;
        if (same && stored.Xero.Checked is not null)
            return (null, null);
        return (updated with { Xero = updated.Xero with { Checked = now } }, news);
    }

    /// <summary>How the invoice or quote stands, for the lists: "Paid", "Part paid", "Accepted", or null when Xero hasn't said.</summary>
    public static string? Standing(StoredDocument d) => d.Settled?.As switch
    {
        Settlement.Deleted => "Deleted in Xero",
        { } settled => Settlements.Label(settled),
        _ when d.Document.Kind == DocumentKind.Invoice && d.Xero.AmountPaid > 0 && d.Xero.AmountDue > 0 => "Part paid",
        _ when XeroStatuses.IsDraft(d.Xero.Status) => "Draft in Xero",
        _ when d.Xero.Status == XeroStatuses.Authorised => "Approved",
        _ when d.Xero.Status == XeroStatuses.Sent => "Sent",
        _ => null,
    };
}
