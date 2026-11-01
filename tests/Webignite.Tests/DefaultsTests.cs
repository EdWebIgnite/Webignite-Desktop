namespace Webignite.Tests;

public class DefaultsTests
{
    [Fact]
    public void New_invoice_gets_date_currency_due_date_and_totals()
    {
        var doc = Samples.Completed(Samples.NewInvoice(Samples.Priced("Hosting", 2, 450m), Samples.Priced("Domain", 1, 99.5m)));

        Assert.Equal(Samples.Today, doc.Date);
        Assert.Equal(Samples.Today.AddDays(7), doc.DueOrExpiryDate);
        Assert.Equal("ZAR", doc.Currency);
        Assert.Equal(999.50m, doc.Subtotal);
        Assert.Equal(0m, doc.TaxTotal);
        Assert.Equal(999.50m, doc.Total);
        Assert.Empty(DocumentValidator.Validate(doc));
    }

    [Fact]
    public void New_quote_uses_expiry_days()
    {
        var doc = Samples.Completed(Samples.NewInvoice() with { Kind = DocumentKind.Quote });
        Assert.Equal(Samples.Today.AddDays(30), doc.DueOrExpiryDate);
    }

    [Fact]
    public void Given_values_are_kept()
    {
        var date = new DateOnly(2026, 11, 3);
        var doc = Samples.Completed(Samples.NewInvoice() with { Date = date, DueOrExpiryDate = date.AddDays(14), Currency = "USD" });

        Assert.Equal(date, doc.Date);
        Assert.Equal(date.AddDays(14), doc.DueOrExpiryDate);
        Assert.Equal("USD", doc.Currency);
    }

    [Fact]
    public void Document_discount_comes_off_the_total()
    {
        var doc = Samples.Completed(Samples.NewInvoice(Samples.Priced("Retainer", 1, 1000m)) with { DocumentDiscountPercent = 12.5m });
        Assert.Equal(1000m, doc.Subtotal);
        Assert.Equal(875m, doc.Total);
        Assert.Empty(DocumentValidator.Validate(doc));
    }

    [Fact]
    public void Imported_documents_are_not_changed()
    {
        var imported = Samples.NewInvoice() with { Origin = DocumentOrigin.PdfImport, Number = "1042" };
        Assert.Same(imported, Samples.Completed(imported));
    }
}
