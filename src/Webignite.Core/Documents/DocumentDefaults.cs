namespace Webignite.Core.Documents;

public static class DocumentDefaults
{
    /// <summary>Days typed for "due after" or "expires after": a whole number from 0 to 365, else null.</summary>
    public static int? ParseDays(string? text) =>
        int.TryParse(text?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days)
        && days is >= 0 and <= 365 ? days : null;

    /// <summary>
    /// When a quote stays valid until, as it goes to the client: <paramref name="days"/> from the day it's sent, unless
    /// someone picked another date by hand (one that isn't the default worked out from the quote's own date).
    /// </summary>
    public static DateOnly QuoteExpiryOnSending(DateOnly? expiry, DateOnly? quoteDate, DateOnly sent, int days) =>
        expiry is { } picked && quoteDate is { } made && picked != made.AddDays(days) && picked > sent ? picked : sent.AddDays(days);

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
