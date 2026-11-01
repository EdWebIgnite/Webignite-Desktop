namespace Webignite.Core.Documents;

public static class DocumentDefaults
{
    /// <summary>
    /// Fills in what nobody should have to type on a new quote or invoice: today's date, the currency,
    /// the due or expiry date from settings, and the totals from the lines. Imported documents are
    /// returned unchanged, because their figures must match the source exactly.
    /// </summary>
    public static Document Complete(Document document, DocumentSettings settings, DateOnly today)
    {
        if (document.Origin != DocumentOrigin.New)
            return document;

        var date = document.Date ?? today;
        var days = document.Kind == DocumentKind.Quote ? settings.QuoteExpiryDays : settings.InvoiceDueDays;
        var subtotal = document.Subtotal ?? Money.Cents(document.LinesTotal());
        var withSubtotal = document with { Subtotal = subtotal };
        var tax = document.TaxTotal ?? 0m;

        return withSubtotal with
        {
            Date = date,
            DueOrExpiryDate = document.DueOrExpiryDate ?? date.AddDays(days),
            Currency = string.IsNullOrWhiteSpace(document.Currency) ? settings.Currency : document.Currency,
            TaxTotal = tax,
            Total = document.Total ?? Money.Cents(subtotal - withSubtotal.Discount() + tax),
        };
    }
}
