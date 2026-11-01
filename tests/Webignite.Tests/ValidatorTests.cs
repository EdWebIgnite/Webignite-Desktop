namespace Webignite.Tests;

public class ValidatorTests
{
    private static IReadOnlyList<string> Errors(Document doc) => DocumentValidator.Validate(doc);

    [Fact]
    public void Wrong_total_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice()) with { Total = 451m }),
            e => e.Contains("do not add up"));

    [Fact]
    public void Tax_is_rejected_for_a_non_vat_business() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice() with { TaxTotal = 67.5m })),
            e => e.Contains("non-VAT"));

    [Fact]
    public void Quantity_without_price_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice(
            Samples.Priced("Hosting", 1, 450m), new Line { Description = "Half", Quantity = 1 }))),
            e => e.StartsWith("Line 2:"));

    [Fact]
    public void Only_text_lines_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice(new Line { Description = "Note" }))),
            e => e.Contains("quantity and price"));

    [Fact]
    public void Both_line_discounts_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice(
            Samples.Priced("Hosting", 1, 450m) with { DiscountPercent = 10, DiscountAmount = 5 }))),
            e => e.Contains("never both"));

    [Fact]
    public void Due_date_before_date_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice() with { DueOrExpiryDate = Samples.Today.AddDays(-1) })),
            e => e.Contains("Due date"));

    [Fact]
    public void Imported_pdf_needs_a_number()
    {
        var doc = Samples.Completed(Samples.NewInvoice()) with { Origin = DocumentOrigin.PdfImport };
        Assert.Contains(Errors(doc), e => e.Contains("original number"));
    }

    [Fact]
    public void Source_line_total_must_match() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice(
            Samples.Priced("Hosting", 1, 450m) with { SourceLineTotal = 455m }))),
            e => e.Contains("source document"));

    [Fact]
    public void Too_many_decimal_places_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice(Samples.Priced("Hours", 1, 1.23456m)))),
            e => e.Contains("decimal places"));

    [Fact]
    public void Trailing_zeros_do_not_count_as_decimal_places() =>
        Assert.Empty(Errors(Samples.Completed(Samples.NewInvoice(Samples.Priced("Hours", 1.5000m, 100.0000m)))));

    [Fact]
    public void Lowercase_currency_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice() with { Currency = "zar" })),
            e => e.Contains("Currency"));

    [Fact]
    public void Long_invoice_reference_is_rejected() =>
        Assert.Contains(Errors(Samples.Completed(Samples.NewInvoice() with { Reference = new string('x', 256) })),
            e => e.Contains("255"));
}
