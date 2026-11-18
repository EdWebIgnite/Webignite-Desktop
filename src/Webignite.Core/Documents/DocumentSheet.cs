using System.Globalization;
using Webignite.Core.Clients;

namespace Webignite.Core.Documents;

/// <summary>One line of a quote or invoice as printed. A line with no amount is words only.</summary>
public sealed record SheetRow(string Description, string? Quantity, string? Price, string? Amount);

/// <summary>A figure under the lines: "Subtotal", "Total".</summary>
public sealed record SheetTotal(string Label, string Value, bool Strong = false);

/// <summary>
/// A quote or invoice as Webignite prints it itself, in words and figures ready to draw: who it's from and to, the
/// lines, the totals, the banking details and the link to pay online. Text a client reads uses
/// <see cref="CultureInfo.InvariantCulture"/>, so it reads the same from any PC.
/// </summary>
public sealed record DocumentSheet
{
    public required string Title { get; init; }
    public required string Number { get; init; }
    public bool IsDraft { get; init; }
    public string? Logo { get; init; }
    public required IReadOnlyList<string> From { get; init; }
    public required IReadOnlyList<string> To { get; init; }
    public required IReadOnlyList<SheetTotal> Facts { get; init; }
    public required IReadOnlyList<SheetRow> Rows { get; init; }
    public required IReadOnlyList<SheetTotal> Totals { get; init; }
    public string? Notes { get; init; }
    public string? Terms { get; init; }
    public IReadOnlyList<string> Bank { get; init; } = [];
    public string? PayLink { get; init; }
    public string? Footer { get; init; }

    public string FileName => $"{(Title == "QUOTE" ? "Quote" : "Invoice")} {Number}.pdf";
}

public static class DocumentSheets
{
    /// <summary>
    /// The sheet for a stored quote or invoice. A client known by a company is addressed as the company, attention
    /// the person; otherwise as the customer on the document.
    /// </summary>
    public static DocumentSheet Build(StoredDocument stored, MailBrand brand, Client? client)
    {
        var doc = stored.Document;
        var quote = doc.Kind == DocumentKind.Quote;
        var number = First(stored.Xero.Number, doc.Number);
        var total = doc.Total ?? Money.Cents(doc.NetSubtotal() + (doc.TaxTotal ?? 0));
        var paid = stored.Xero.AmountPaid ?? doc.AmountPaid ?? 0;
        var due = quote ? 0 : stored.Xero.AmountDue ?? doc.BalanceDue ?? Money.Cents(total - paid);

        var facts = new List<SheetTotal>();
        if (doc.Date is { } date)
            facts.Add(new("Date", Day(date)));
        if (doc.DueOrExpiryDate is { } until)
            facts.Add(new(quote ? "Valid until" : "Due", Day(until)));
        if (!string.IsNullOrWhiteSpace(doc.Reference))
            facts.Add(new("Reference", doc.Reference.Trim()));

        var totals = new List<SheetTotal>();
        var discount = doc.Discount();
        if (discount != 0)
        {
            totals.Add(new("Subtotal", Money.Rand(doc.Subtotal ?? doc.LinesTotal())));
            totals.Add(new(doc.DocumentDiscountPercent is { } percent ? $"Discount ({percent.ToString("0.##", CultureInfo.InvariantCulture)}%)" : "Discount", Money.Rand(-discount)));
        }
        totals.Add(new("Total", Money.Rand(total), Strong: quote || paid == 0));
        if (!quote && paid != 0)
        {
            totals.Add(new("Paid", Money.Rand(-paid)));
            totals.Add(new("Amount due", Money.Rand(due), Strong: true));
        }

        return new DocumentSheet
        {
            Title = quote ? "QUOTE" : "INVOICE",
            Number = number ?? "Draft",
            IsDraft = number is null,
            Logo = brand.Logo,
            From = Lines(brand.CompanyName, brand.Address, brand.Phone, brand.Email, brand.Website),
            To = Addressed(doc.Customer, client),
            Facts = facts,
            Rows = [.. doc.Lines.Select(Row)],
            Totals = totals,
            Notes = Blank(doc.Notes),
            Terms = Blank(doc.Terms) ?? (Blank(brand.TermsLink) is { } link ? "Terms and conditions: " + link : null),
            Bank = quote ? [] : Lines(brand.BankDetails),
            PayLink = quote || due <= 0 || number is null ? null : PayFast.Link(brand.PayFastMerchantId, due, "Invoice " + number, number),
            Footer = Blank(brand.CompanyName) is { } name ? "Thank you for your business. " + name : null,
        };
    }

    /// <summary>Who it's to: the company with "Attention" for the person when the client has both, then the address.</summary>
    public static IReadOnlyList<string> Addressed(Customer customer, Client? client)
    {
        var lines = new List<string>();
        if (client is not null && client.Called != client.Name)
        {
            lines.Add(client.Called);
            lines.Add("Attention: " + client.Name.Trim());
        }
        else
        {
            lines.Add(customer.Name.Trim());
            if (Blank(customer.Attention ?? client?.ContactPerson) is { } person && !string.Equals(person, customer.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                lines.Add("Attention: " + person);
        }
        var address = customer.AddressLines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        if (address.Count == 0 && client is not null)
            address = [.. Lines(client.Address)];
        lines.AddRange(address);
        if (string.Join(" ", new[] { customer.City, customer.Region, customer.PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())) is { Length: > 0 } town)
            lines.Add(town);
        if (Blank(customer.Country) is { } country)
            lines.Add(country);
        return lines;
    }

    private static SheetRow Row(Line line)
    {
        var words = Blank(line.Product) is { } product && !line.Description.Trim().StartsWith(product, StringComparison.OrdinalIgnoreCase)
            ? product + "\n" + line.Description.Trim() : line.Description.Trim();
        if (!line.IsPriced)
            return new SheetRow(words, null, null, null);
        var discount = line.DiscountPercent is { } percent && percent != 0 ? $"\nLess {percent.ToString("0.##", CultureInfo.InvariantCulture)}%" : "";
        return new SheetRow(words + discount, line.Quantity!.Value.ToString("0.##", CultureInfo.InvariantCulture),
            line.UnitPrice is { } price ? Money.Rand(price) : null, Money.Rand(line.Amount()));
    }

    private static string Day(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Lines(params string?[] blocks) =>
        [.. blocks.Where(b => !string.IsNullOrWhiteSpace(b)).SelectMany(b => b!.Split('\n')).Select(l => l.Trim()).Where(l => l.Length > 0)];

    private static string? First(params string?[] values) => values.Select(Blank).FirstOrDefault(v => v is not null);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>PayFast's "Pay Now" link: a web address that opens PayFast with the amount and what it's for filled in.</summary>
public static class PayFast
{
    /// <summary>The link, or null without a merchant ID or an amount to pay. The ID is digits only.</summary>
    public static string? Link(string? merchantId, decimal amount, string what, string reference)
    {
        var id = new string([.. (merchantId ?? "").Where(char.IsAsciiDigit)]);
        if (id.Length == 0 || amount <= 0)
            return null;
        return "https://www.payfast.co.za/eng/process?cmd=_paynow"
            + "&receiver=" + id
            + "&item_name=" + Uri.EscapeDataString(what)
            + "&m_payment_id=" + Uri.EscapeDataString(reference)
            + "&amount=" + Money.Cents(amount).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
