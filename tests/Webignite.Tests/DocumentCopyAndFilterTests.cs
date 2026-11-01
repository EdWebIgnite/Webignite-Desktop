namespace Webignite.Tests;

public sealed class DocumentCopyAndFilterTests
{
    private static StoredDocument Quote(string number, string reference = "") => new()
    {
        Id = "q1",
        XeroId = "xq1",
        XeroNumber = number,
        XeroContactId = "contact-1",
        Status = DocumentStatus.InXero,
        Approved = DateTimeOffset.UtcNow,
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("New website, 6 pages", 1, 14500m))) with
        {
            Kind = DocumentKind.Quote,
            Date = new DateOnly(2026, 9, 1),
            Reference = reference.Length > 0 ? reference : null,
            Notes = "Half up front.",
        },
    };

    [Fact]
    public void A_copy_is_a_fresh_draft_with_the_same_customer_and_lines()
    {
        var copy = DocumentCopies.Copy(Quote("QU-0031"), DocumentKind.Quote, Samples.Settings, Samples.Today);

        Assert.NotEqual("q1", copy.Id);
        Assert.Equal(DocumentStatus.Draft, copy.Status);
        Assert.Null(copy.XeroId);
        Assert.Null(copy.XeroNumber);
        Assert.Null(copy.Approved);
        Assert.Equal("contact-1", copy.XeroContactId);
        Assert.Equal("Karoo Coffee Roasters", copy.Document.Customer.Name);
        Assert.Equal("New website, 6 pages", Assert.Single(copy.Document.Lines).Description);
        Assert.Equal("Half up front.", copy.Document.Notes);
        Assert.Equal(Samples.Today, copy.Document.Date);
        Assert.Equal(14500m, copy.Document.Total);
    }

    [Fact]
    public void An_invoice_from_a_quote_says_which_quote()
    {
        var invoice = DocumentCopies.Copy(Quote("QU-0031"), DocumentKind.Invoice, Samples.Settings, Samples.Today);

        Assert.Equal(DocumentKind.Invoice, invoice.Document.Kind);
        Assert.Equal("QU-0031", invoice.Document.Reference);
        Assert.Equal(Samples.Today.AddDays(Samples.Settings.InvoiceDueDays), invoice.Document.DueOrExpiryDate);
    }

    [Fact]
    public void A_quote_with_its_own_reference_keeps_it_on_the_invoice()
    {
        var invoice = DocumentCopies.Copy(Quote("QU-0031", "Bosveld site"), DocumentKind.Invoice, Samples.Settings, Samples.Today);
        Assert.Equal("Bosveld site", invoice.Document.Reference);
    }

    private static StoredDocument Invoice(string customer, decimal price, DateOnly date, bool inXero, string? number = null, string line = "Monthly SEO") => new()
    {
        Id = customer,
        XeroNumber = number,
        Status = inXero ? DocumentStatus.InXero : DocumentStatus.Draft,
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced(line, 1, price)) with { Date = date }) with
        {
            Customer = new Customer { Name = customer },
        },
    };

    private static readonly StoredDocument[] List =
    [
        Invoice("Karoo Coffee Roasters", 450m, new DateOnly(2026, 10, 3), true, "INV-0107", "Website hosting"),
        Invoice("Fynbos Physio", 2800m, new DateOnly(2026, 9, 20), true, "INV-0101"),
        Invoice("Bosveld Builders", 14500m, new DateOnly(2026, 2, 10), false),
    ];

    private static IEnumerable<string> Names(DocumentFilter filter) => filter.Apply(List, Samples.Today).Select(d => d.Document.Customer.Name);

    [Fact]
    public void Words_find_customer_number_reference_or_line()
    {
        Assert.Equal(["Karoo Coffee Roasters"], Names(new("karoo")));
        Assert.Equal(["Fynbos Physio"], Names(new("inv-0101")));
        Assert.Equal(["Karoo Coffee Roasters"], Names(new("hosting")));
        Assert.Equal(["Fynbos Physio", "Bosveld Builders"], Names(new("seo")));
        Assert.Empty(Names(new("karoo seo")));
    }

    [Fact]
    public void Amounts_find_the_total_however_they_are_typed()
    {
        Assert.Equal(["Fynbos Physio"], Names(new("2800")));
        Assert.Equal(["Fynbos Physio"], Names(new("R2 800,00")));
        Assert.Equal(["Bosveld Builders"], Names(new("14 500")));
    }

    [Fact]
    public void Filters_by_where_it_stands_and_when()
    {
        Assert.Equal(["Bosveld Builders"], Names(new(Show: DocumentShow.NotInXero)));
        Assert.Equal(["Karoo Coffee Roasters"], Names(new(Period: DocumentPeriod.ThisMonth)));
        Assert.Equal(["Fynbos Physio"], Names(new(Period: DocumentPeriod.LastMonth)));
        // Tax year runs 1 March 2026 to 28 February 2027, so February 2026 is last year's.
        Assert.Equal(["Karoo Coffee Roasters", "Fynbos Physio"], Names(new(Period: DocumentPeriod.ThisTaxYear)));
        Assert.False(new DocumentFilter().IsActive);
        Assert.True(new DocumentFilter(Show: DocumentShow.InXero).IsActive);
    }

    [Fact]
    public void Tax_year_starts_in_March()
    {
        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28)), DocumentFilter.TaxYear(new DateOnly(2026, 11, 1)));
        Assert.Equal((new DateOnly(2027, 3, 1), new DateOnly(2028, 2, 29)), DocumentFilter.TaxYear(new DateOnly(2027, 3, 1)));
        Assert.Equal((new DateOnly(2025, 3, 1), new DateOnly(2026, 2, 28)), DocumentFilter.TaxYear(new DateOnly(2026, 2, 28)));
    }
}
