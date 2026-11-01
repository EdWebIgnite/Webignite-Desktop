using System.Text.RegularExpressions;

namespace Webignite.Core.Documents;

/// <summary>
/// Checks a completed document before it can go anywhere near Xero. Every rule here is a reason a
/// document could be wrong, so problems are reported in plain words rather than fixed silently.
/// </summary>
public static partial class DocumentValidator
{
    public const int MaxLines = 200;
    public const int MaxInvoiceReference = 255;
    private const decimal MaxAmount = 9_999_999_999m;

    public static IReadOnlyList<string> Validate(Document doc)
    {
        var errors = new List<string>();

        if (doc.SchemaVersion != 1)
            errors.Add("Unsupported document version.");
        CheckCustomer(doc.Customer, errors);

        if (doc.Lines.Count == 0)
            errors.Add("Add at least one line.");
        if (doc.Lines.Count > MaxLines)
            errors.Add($"A document can have at most {MaxLines} lines.");
        for (var i = 0; i < doc.Lines.Count; i++)
            CheckLine(i + 1, doc.Lines[i], errors);
        if (doc.Lines.Count > 0 && !doc.Lines.Any(line => line.IsPriced))
            errors.Add("At least one line needs a quantity and price.");

        Text(doc.Number, 255, "Number", errors);
        Text(doc.Reference, 4000, "Reference", errors);
        Text(doc.Notes, 3000, "Notes", errors);
        Text(doc.Terms, 4000, "Terms", errors);
        Text(doc.OriginalStatus, 100, "Original status", errors);

        if (doc.Origin == DocumentOrigin.PdfImport && string.IsNullOrWhiteSpace(doc.Number))
            errors.Add("An imported PDF needs its original number. Flag missing numbers for review.");
        if (doc.Kind == DocumentKind.Invoice && (doc.Reference?.Length ?? 0) > MaxInvoiceReference)
            errors.Add($"Invoice reference is longer than Xero's {MaxInvoiceReference}-character limit.");

        if (doc.Date is null)
            errors.Add("Date is missing.");
        if (doc.Date is { } date && doc.DueOrExpiryDate is { } due && due < date)
            errors.Add(doc.Kind == DocumentKind.Quote ? "Expiry date is before the quote date." : "Due date is before the invoice date.");
        if (doc.Currency is null || !CurrencyCode().IsMatch(doc.Currency))
            errors.Add("Currency must be a three-letter code such as ZAR.");

        if (doc.Subtotal is null || doc.TaxTotal is null || doc.Total is null)
        {
            errors.Add("Totals are missing.");
            return errors;
        }

        Amount(doc.Subtotal, "Subtotal", 2, errors, nonNegative: true);
        Amount(doc.TaxTotal, "Tax", 2, errors, nonNegative: true);
        Amount(doc.Total, "Total", 2, errors, nonNegative: true);
        Amount(doc.DocumentDiscountAmount, "Document discount", 2, errors, nonNegative: true);
        Percent(doc.DocumentDiscountPercent, "Document discount", errors);
        Amount(doc.AmountPaid, "Amount paid", 2, errors, nonNegative: true);
        Amount(doc.BalanceDue, "Balance due", 2, errors, nonNegative: true);

        if (doc.TaxTotal != 0)
            errors.Add("Only non-VAT sales are supported. Do not remove tax to force a document through.");
        if (doc.DocumentDiscountAmount is not null && doc.DocumentDiscountPercent is not null)
            errors.Add("Use a document discount amount or a percentage, not both.");
        if (doc.Discount() > doc.Subtotal)
            errors.Add("The document discount is more than the subtotal.");
        if (Money.Cents(doc.LinesTotal()) != Money.Cents(doc.Subtotal.Value)
            || Money.Cents(doc.NetSubtotal() + doc.TaxTotal.Value) != Money.Cents(doc.Total.Value))
            errors.Add("Line totals, subtotal, tax and total do not add up exactly.");
        if (doc.AmountPaid is { } paid && doc.BalanceDue is { } balance && Money.Cents(paid + balance) != Money.Cents(doc.Total.Value))
            errors.Add("Amount paid and balance due do not add up to the total.");

        return errors;
    }

    private static void CheckCustomer(Customer customer, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(customer.Name))
            errors.Add("Customer name is blank.");
        Text(customer.Name, 255, "Customer name", errors);
        Text(customer.Email, 255, "Customer email", errors);
        Text(customer.Phone, 50, "Customer phone", errors);
        Text(customer.Attention, 255, "Attention", errors);
        if (customer.AddressLines.Count > 4)
            errors.Add("A customer address can have at most 4 lines.");
    }

    private static void CheckLine(int n, Line line, List<string> errors)
    {
        var label = $"Line {n}";
        if (string.IsNullOrWhiteSpace(line.Description))
            errors.Add($"{label}: description is blank.");
        Text(line.Description, 4000, $"{label} description", errors);
        Text(line.Product, 1000, $"{label} product", errors);
        Text(line.ItemCode, 30, $"{label} item code", errors);

        if (line.Quantity is null != line.UnitPrice is null)
            errors.Add($"{label}: give both a quantity and a unit price, or neither for a text-only line.");
        if (line.DiscountPercent is not null && line.DiscountAmount is not null)
            errors.Add($"{label}: use a discount percentage or an amount, never both.");
        if (line.Quantity is null && (line.DiscountPercent is not null || line.DiscountAmount is not null || line.SourceLineTotal is not null))
            errors.Add($"{label}: a text-only line cannot have amounts or discounts.");

        Amount(line.Quantity, $"{label} quantity", 4, errors);
        Amount(line.UnitPrice, $"{label} unit price", 4, errors);
        Percent(line.DiscountPercent, $"{label} discount", errors);
        Amount(line.DiscountAmount, $"{label} discount", 2, errors, nonNegative: true);
        Amount(line.SourceLineTotal, $"{label} source total", 2, errors);

        if (line.SourceLineTotal is { } source && Money.Cents(source) != line.Amount())
            errors.Add($"{label}: amount does not match the line total on the source document.");
    }

    private static void Text(string? value, int max, string field, List<string> errors)
    {
        if (value is not null && value.Length > max)
            errors.Add($"{field} is longer than {max} characters.");
    }

    private static void Amount(decimal? value, string field, int places, List<string> errors, bool nonNegative = false)
    {
        if (value is not { } v)
            return;
        if (nonNegative && v < 0)
            errors.Add($"{field} cannot be negative.");
        if (Math.Abs(v) > MaxAmount)
            errors.Add($"{field} is too large.");
        if (DecimalPlaces(v) > places)
            errors.Add($"{field} has more than {places} decimal places.");
    }

    private static void Percent(decimal? value, string field, List<string> errors)
    {
        if (value is not { } v)
            return;
        if (v is < 0 or > 100)
            errors.Add($"{field} percentage must be between 0 and 100.");
        if (DecimalPlaces(v) > 4)
            errors.Add($"{field} percentage has more than 4 decimal places.");
    }

    /// <summary>Decimal places ignoring trailing zeros, so 1.50 counts as 1 place.</summary>
    internal static int DecimalPlaces(decimal value) => (value / 1.0000000000000000000000000000m).Scale;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
