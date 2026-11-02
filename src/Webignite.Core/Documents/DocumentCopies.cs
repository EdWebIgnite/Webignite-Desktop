namespace Webignite.Core.Documents;

/// <summary>
/// Starts a new draft from an existing quote or invoice: a copy of the same kind, or an invoice from a quote.
/// The copy keeps the customer, lines, reference and notes. Dates, numbers and anything Xero knows about the
/// original are left behind, so the copy gets today's date and the next number when it goes to Xero.
/// </summary>
public static class DocumentCopies
{
    public static StoredDocument Copy(StoredDocument source, DocumentKind kind, DocumentSettings settings, DateOnly today)
    {
        var from = source.Document;
        var reference = from.Reference;
        // An invoice made from a quote says which quote, unless the quote already had its own reference.
        if (kind == DocumentKind.Invoice && from.Kind == DocumentKind.Quote && string.IsNullOrWhiteSpace(reference) && source.XeroNumber is { } quoteNumber)
            reference = quoteNumber;

        var document = DocumentDefaults.Complete(new Document
        {
            Kind = kind,
            Origin = DocumentOrigin.New,
            Customer = from.Customer,
            Reference = reference,
            Notes = from.Notes,
            Terms = from.Terms,
            Currency = from.Currency,
            DocumentDiscountAmount = from.DocumentDiscountAmount,
            DocumentDiscountPercent = from.DocumentDiscountPercent,
            // Totals copied from an import would be stale, so lines keep only what they are made of.
            Lines = from.Lines.Select(line => line with { SourceLineTotal = null }).ToList(),
        }, settings, today);

        return new StoredDocument
        {
            Id = StoredDocument.NewId(),
            Document = document,
            Status = DocumentStatus.Draft,
            Source = DocumentSource.App,
            // Same customer, so the same Xero contact: no need to ask again.
            XeroContactId = source.XeroContactId,
        };
    }
}
