using System.Globalization;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Xero;

/// <summary>Reads quotes and sales invoices from Xero, for bringing in ones made in Xero itself. Read only.</summary>
public sealed class XeroDocumentReader(XeroApi api)
{
    /// <summary>
    /// The first look: invoices dated within <see cref="XeroDocuments.InvoiceMonths"/> plus any still owed, and quotes
    /// dated within <see cref="XeroDocuments.QuoteMonths"/>. Later looks: only what Xero changed since then.
    /// </summary>
    public async Task<IReadOnlyList<XeroSalesDocument>> ReadAsync(DateOnly today, DateTimeOffset? changedSince, CancellationToken cancel = default)
    {
        var rows = new List<XeroSalesDocument>();
        if (changedSince is { } since)
        {
            var from = since.UtcDateTime.AddDays(-1);
            var stamp = $"DateTime({from.Year},{from.Month:00},{from.Day:00})";
            rows.AddRange((await api.AllAsync("Invoices", Where($"Type==\"ACCREC\"&&UpdatedDateUTC>={stamp}"), cancel)).Select(ToInvoice));
            rows.AddRange((await api.AllAsync("Quotes", "&DateFrom=" + Iso(today.AddMonths(-XeroDocuments.QuoteMonths)), cancel))
                .Where(q => XeroPayload.NormalizeDate(XeroApi.Str(q["UpdatedDateUTC"])) is { } u && string.CompareOrdinal(u, Iso(DateOnly.FromDateTime(from))) >= 0)
                .Select(ToQuote));
            return rows;
        }
        var cutoff = today.AddMonths(-XeroDocuments.InvoiceMonths);
        rows.AddRange((await api.AllAsync("Invoices", Where($"Type==\"ACCREC\"&&Date>=DateTime({cutoff.Year},{cutoff.Month:00},{cutoff.Day:00})"), cancel)).Select(ToInvoice));
        rows.AddRange((await api.AllAsync("Invoices", "&Statuses=AUTHORISED" + Where("Type==\"ACCREC\""), cancel)).Select(ToInvoice));
        rows.AddRange((await api.AllAsync("Quotes", "&DateFrom=" + Iso(today.AddMonths(-XeroDocuments.QuoteMonths)), cancel)).Select(ToQuote));
        return rows;
    }

    private static string Where(string condition) => "&where=" + Uri.EscapeDataString(condition);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static XeroSalesDocument ToInvoice(JsonObject r) => Read(r, DocumentKind.Invoice) with
    {
        SentToContact = r["SentToContact"] is JsonValue v && v.TryGetValue<bool>(out var sent) && sent,
    };

    public static XeroSalesDocument ToQuote(JsonObject r) => Read(r, DocumentKind.Quote) with
    {
        Summary = XeroApi.Str(r["Summary"]) is { Length: > 0 } s ? s : null,
        Terms = XeroApi.Str(r["Terms"]) is { Length: > 0 } t ? t : null,
    };

    private static XeroSalesDocument Read(JsonObject r, DocumentKind kind)
    {
        var quote = kind == DocumentKind.Quote;
        var standing = XeroStandings.ToStanding(r);
        return new XeroSalesDocument
        {
            Id = XeroApi.Str(r[quote ? "QuoteID" : "InvoiceID"]),
            Kind = kind,
            Number = XeroApi.Str(r[quote ? "QuoteNumber" : "InvoiceNumber"]) is { Length: > 0 } n ? n : null,
            ContactId = XeroApi.Str(r["Contact"]?["ContactID"]),
            ContactName = XeroApi.Str(r["Contact"]?["Name"]),
            Date = Day(r["DateString"]) ?? Day(r["Date"]),
            DueOrExpiry = quote ? Day(r["ExpiryDateString"]) ?? Day(r["ExpiryDate"]) : Day(r["DueDateString"]) ?? Day(r["DueDate"]),
            Reference = XeroApi.Str(r["Reference"]) is { Length: > 0 } reference ? reference : null,
            Currency = XeroApi.Str(r["CurrencyCode"]) is { Length: > 0 } c ? c : null,
            Lines = (r["LineItems"] as JsonArray ?? []).OfType<JsonObject>().Select(ToLine).ToList(),
            Subtotal = Amount(r["SubTotal"]),
            Tax = Amount(r["TotalTax"]),
            Total = Amount(r["Total"]),
            Status = standing.Status,
            AmountDue = standing.AmountDue,
            AmountPaid = standing.AmountPaid,
            PaidOn = standing.PaidOn,
        };
    }

    private static Line ToLine(JsonObject l)
    {
        var quantity = Amount(l["Quantity"]);
        var price = Amount(l["UnitAmount"]);
        var priced = quantity is not null && price is not null && (quantity != 0 || price != 0);
        return new Line
        {
            Description = XeroApi.Str(l["Description"]),
            Quantity = priced ? quantity : null,
            UnitPrice = priced ? price : null,
            DiscountPercent = priced && Amount(l["DiscountRate"]) is > 0 and var rate ? rate : null,
            DiscountAmount = priced && Amount(l["DiscountAmount"]) is > 0 and var off ? off : null,
            ItemCode = XeroApi.Str(l["ItemCode"]) is { Length: > 0 } code ? code : null,
            Product = XeroApi.Str(l["Item"]?["Name"]) is { Length: > 0 } item ? item : XeroApi.Str(l["ItemCode"]) is { Length: > 0 } c ? c : null,
            SourceLineTotal = Amount(l["LineAmount"]),
        };
    }

    private static DateOnly? Day(JsonNode? node) =>
        XeroPayload.NormalizeDate(XeroApi.Str(node)) is { Length: 10 } iso && DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Amount(JsonNode? node) => node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;
}
