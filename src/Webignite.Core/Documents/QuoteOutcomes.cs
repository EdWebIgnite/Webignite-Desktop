namespace Webignite.Core.Documents;

/// <summary>What became of a quote: accepted or declined by the client, and the invoices made from it.</summary>
public static class QuoteOutcomes
{
    /// <summary>
    /// A quote in Xero that went to the client and hasn't been settled, so Eddie can say the client accepted or
    /// declined it. Xero only takes either from a sent quote.
    /// </summary>
    public static bool CanDecide(StoredDocument stored) =>
        stored.Document.Kind == DocumentKind.Quote
        && stored.Xero.Id is not null
        && stored.Xero.State is Xero.XeroWriteState.Verified or Xero.XeroWriteState.Mismatch
        && stored.Settled is null
        && (stored.Delivery.Emailed is not null || stored.Xero.Status == XeroStatuses.Sent);

    /// <summary>The quote as it stands once Xero has taken the client's answer.</summary>
    public static StoredDocument Decided(StoredDocument stored, bool accepted, DateTimeOffset now) => stored with
    {
        Xero = stored.Xero with { Status = accepted ? XeroStatuses.Accepted : XeroStatuses.Declined },
        Settled = new SettledState { On = now, As = accepted ? Settlement.Accepted : Settlement.Declined },
    };

    /// <summary>Invoices made from this quote here, oldest first.</summary>
    public static IReadOnlyList<StoredDocument> InvoicesFrom(StoredDocument quote, IEnumerable<StoredDocument> invoices) =>
        invoices.Where(i => i.Document.Kind == DocumentKind.Invoice && i.FromQuote == quote.Id).OrderBy(i => i.Created).ToList();

    /// <summary>How a document is named in a link to it: its Xero number, else "(draft)".</summary>
    public static string Name(StoredDocument stored) =>
        stored.Xero.Number ?? stored.Document.Number ?? "(draft)";
}
