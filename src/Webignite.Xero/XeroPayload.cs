using System.Globalization;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;

namespace Webignite.Xero;

/// <summary>
/// Turns a checked document into the JSON Xero expects for a DRAFT quote or sales invoice, and
/// checks what Xero saved against what was sent. Nothing here ever sends, approves or emails.
/// </summary>
public static class XeroPayload
{
    /// <summary>
    /// A new Xero contact for a customer: name, email, phone and the postal address an assistant or import gave.
    /// Only used when Webignite makes a contact; an existing Xero contact is never changed. The postal address
    /// (POBOX in Xero) is the one Xero prints on invoices.
    /// </summary>
    public static JsonObject NewContact(Customer customer, string? email)
    {
        var contact = new JsonObject { ["Name"] = customer.Name.Trim() };
        // Xero holds one address per contact: the first, when the customer has several.
        if (Webignite.Core.Mail.ClientMail.First(email ?? customer.Email) is { } address)
            contact["EmailAddress"] = address;
        if (!string.IsNullOrWhiteSpace(customer.Phone))
            contact["Phones"] = new JsonArray(new JsonObject { ["PhoneType"] = "DEFAULT", ["PhoneNumber"] = customer.Phone.Trim() });

        var postal = new JsonObject { ["AddressType"] = "POBOX" };
        void Put(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                postal[key] = value.Trim();
        }
        Put("AttentionTo", customer.Attention);
        var lines = customer.AddressLines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(4).ToList();
        for (var i = 0; i < lines.Count; i++)
            Put($"AddressLine{i + 1}", lines[i]);
        Put("City", customer.City);
        Put("Region", customer.Region);
        Put("PostalCode", customer.PostalCode);
        Put("Country", customer.Country);
        if (postal.Count > 1)
            contact["Addresses"] = new JsonArray(postal);
        return contact;
    }

    /// <summary>
    /// The draft for Xero. With <paramref name="brandingThemeId"/> it uses that theme (and so its payment services,
    /// such as Payfast's Pay now); without one, Xero uses the organisation's default theme.
    /// </summary>
    public static JsonObject Build(Document doc, string contactId, string accountCode, string taxType, string? number = null, string? brandingThemeId = null)
    {
        var quote = doc.Kind == DocumentKind.Quote;
        var lines = Lines(doc, accountCode, taxType);
        var payload = Payload(doc, contactId, lines, quote);
        if (!string.IsNullOrEmpty(number))
            payload[quote ? "QuoteNumber" : "InvoiceNumber"] = number;
        if (!string.IsNullOrEmpty(brandingThemeId))
            payload["BrandingThemeID"] = brandingThemeId;
        if (doc.DueOrExpiryDate is { } due)
            payload[quote ? "ExpiryDate" : "DueDate"] = Iso(due);

        if (quote)
        {
            payload["Summary"] = doc.Notes ?? "";
            payload["Terms"] = doc.Terms ?? "";
        }
        else
        {
            payload["Type"] = "ACCREC";
            // Invoices have no notes or terms fields in the API, so they go in as text-only lines.
            foreach (var (label, value) in new[] { ("Notes", doc.Notes), ("Terms and conditions", doc.Terms) })
                if (!string.IsNullOrEmpty(value))
                    lines.Add(new JsonObject { ["Description"] = label + ":\n" + value });
        }
        return payload;
    }

    private static JsonObject Payload(Document doc, string contactId, JsonArray lines, bool quote) => new()
    {
        ["Contact"] = new JsonObject { ["ContactID"] = contactId },
        ["Date"] = Iso(doc.Date ?? throw new InvalidOperationException("Document has no date.")),
        ["Status"] = XeroStatuses.Draft,
        ["CurrencyCode"] = doc.Currency,
        ["LineItems"] = lines,
        // Xero spells this differently for quotes and invoices.
        ["LineAmountTypes"] = quote ? "NOTAX" : "NoTax",
        ["Reference"] = doc.Reference ?? "",
    };

    /// <summary>
    /// A DRAFT credit note against an invoice: its lines when the whole invoice is credited, else one line for the
    /// amount and reason. Approving it and allocating it to the invoice are left to Eddie in Xero.
    /// </summary>
    public static JsonObject CreditNote(Document invoice, string invoiceNumber, string contactId, string accountCode, string taxType,
        DateOnly date, decimal? amount, string? reason, string? brandingThemeId = null)
    {
        var lines = amount is null
            ? Lines(invoice, accountCode, taxType)
            : new JsonArray(new JsonObject
            {
                ["Description"] = string.IsNullOrWhiteSpace(reason) ? "Credit against " + invoiceNumber : reason.Trim(),
                ["Quantity"] = 1m,
                ["UnitAmount"] = amount.Value,
                ["AccountCode"] = accountCode,
                ["TaxType"] = taxType,
            });
        var payload = Payload(invoice with { Date = date }, contactId, lines, quote: false);
        payload["Type"] = "ACCRECCREDIT";
        payload["Reference"] = string.IsNullOrWhiteSpace(reason) ? invoiceNumber : invoiceNumber + ": " + reason.Trim();
        if (!string.IsNullOrEmpty(brandingThemeId))
            payload["BrandingThemeID"] = brandingThemeId;
        return payload;
    }

    // Each priced or text line, then the whole-document discount as a line of its own.
    private static JsonArray Lines(Document doc, string accountCode, string taxType)
    {
        var lines = new JsonArray();
        foreach (var line in doc.Lines)
        {
            // A line linked to a Xero item shows the item in Xero's Item column; otherwise its name heads the description.
            var description = line.Description;
            if (string.IsNullOrEmpty(line.ItemCode) && !string.IsNullOrEmpty(line.Product) && !description.StartsWith(line.Product, StringComparison.Ordinal))
                description = line.Product + "\n" + description;

            var item = new JsonObject { ["Description"] = description };
            if (line.Quantity is { } quantity && line.UnitPrice is { } price)
            {
                item["Quantity"] = quantity;
                item["UnitAmount"] = price;
                item["AccountCode"] = accountCode;
                item["TaxType"] = taxType;
                if (!string.IsNullOrEmpty(line.ItemCode))
                    item["ItemCode"] = line.ItemCode;
                if (line.DiscountPercent is { } rate)
                    item["DiscountRate"] = rate;
                if (line.DiscountAmount is { } amount)
                    item["DiscountAmount"] = amount;
            }
            lines.Add(item);
        }

        var discount = doc.Discount();
        if (discount != 0)
        {
            var label = DocumentDiscount.Label(doc);
            lines.Add(new JsonObject
            {
                ["Description"] = label,
                ["Quantity"] = 1m,
                ["UnitAmount"] = -discount,
                ["AccountCode"] = accountCode,
                ["TaxType"] = taxType,
            });
        }
        return lines;
    }


    /// <summary>Compares what Xero saved with what was sent. An empty list means they match.</summary>
    /// <summary>
    /// Whether Xero kept the amounts the way they were sent. Xero doesn't always say "no tax" back the way it was
    /// sent on a quote (it can leave it out or give its own default), so a draft sent without tax counts as the same
    /// when Xero has no tax on it at all: a tax total of nothing and no line with a tax rate.
    /// </summary>
    private static bool SameAmountTypes(string sent, string received, JsonObject saved)
    {
        if (string.Equals(sent, received, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(sent, "NoTax", StringComparison.OrdinalIgnoreCase))
            return false;
        var noTax = (Dec(saved["TotalTax"]) ?? 0m) == 0m;
        var lines = saved["LineItems"] as JsonArray ?? [];
        return noTax && lines.OfType<JsonObject>().All(l => Str(l["TaxType"]) is "" or "NONE" && (Dec(l["TaxAmount"]) ?? 0m) == 0m);
    }

    public static IReadOnlyList<string> Verify(Document doc, JsonObject sent, JsonObject received)
    {
        var errors = new List<string>();

        foreach (var (key, expected) in new[] { ("SubTotal", doc.NetSubtotal()), ("TotalTax", doc.TaxTotal ?? 0m), ("Total", doc.Total ?? 0m) })
            if (Dec(received[key]) is not { } actual || Money.Cents(actual) != Money.Cents(expected))
                errors.Add($"{key} differs from the document.");

        foreach (var key in new[] { "Status", "CurrencyCode", "Reference", "QuoteNumber", "InvoiceNumber", "Terms", "Summary", "Type" })
            if (sent.ContainsKey(key) && Str(received[key]) != Str(sent[key]))
                errors.Add($"{key} differs from the approved document.");

        if (!SameAmountTypes(Str(sent["LineAmountTypes"]), Str(received["LineAmountTypes"]), received))
            errors.Add($"LineAmountTypes differs (Xero has {(Str(received["LineAmountTypes"]) is { Length: > 0 } got ? got : "none")}).");
        if (Str(received["Contact"]?["ContactID"]) != Str(sent["Contact"]?["ContactID"]))
            errors.Add("Customer differs.");

        foreach (var key in new[] { "Date", "DueDate", "ExpiryDate" })
            if (sent.ContainsKey(key) && NormalizeDate(Str(received[key + "String"]) is { Length: > 0 } s ? s : Str(received[key])) != Str(sent[key]))
                errors.Add($"{key} differs.");

        var expectedLines = sent["LineItems"]!.AsArray();
        var actualLines = received["LineItems"] as JsonArray ?? [];
        if (expectedLines.Count != actualLines.Count)
            errors.Add("Line count differs.");

        for (var i = 0; i < Math.Min(expectedLines.Count, actualLines.Count); i++)
        {
            var n = i + 1;
            var expected = expectedLines[i]!.AsObject();
            var actual = actualLines[i] as JsonObject ?? [];
            foreach (var key in new[] { "Description", "AccountCode", "TaxType", "ItemCode" })
                if (expected.ContainsKey(key) && Str(actual[key]) != Str(expected[key]))
                    errors.Add($"Line {n}: {key} differs.");
            foreach (var key in new[] { "Quantity", "UnitAmount", "DiscountRate", "DiscountAmount" })
                if (expected.ContainsKey(key) && (Dec(actual[key]) ?? 0m) != Dec(expected[key]))
                    errors.Add($"Line {n}: {key} differs.");
            if (expected.ContainsKey("Quantity"))
            {
                var amount = Dec(expected["Quantity"])!.Value * Dec(expected["UnitAmount"])!.Value;
                amount *= (100 - (Dec(expected["DiscountRate"]) ?? 0m)) / 100;
                amount -= Dec(expected["DiscountAmount"]) ?? 0m;
                if (Dec(actual["LineAmount"]) is not { } lineAmount || Money.Cents(lineAmount) != Money.Cents(amount))
                    errors.Add($"Line {n}: line total differs.");
            }
        }
        return errors;
    }

    /// <summary>Xero returns dates as /Date(1735689600000+0000)/ or as ISO text.</summary>
    public static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.StartsWith("/Date(", StringComparison.Ordinal))
        {
            var digits = new string(value[6..].TakeWhile(c => c == '-' || char.IsDigit(c)).ToArray());
            var instant = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(digits, CultureInfo.InvariantCulture));
            return Iso(DateOnly.FromDateTime(instant.UtcDateTime));
        }
        return value.Length >= 10 ? value[..10] : value;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node?.ToString() ?? "";

    private static decimal? Dec(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<decimal>(out var d))
            return d;
        if (value.TryGetValue<double>(out var f))
            return decimal.Parse(f.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (value.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }
}
