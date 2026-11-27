using Webignite.Core.Documents;

namespace Webignite.Tests;

/// <summary>Old quotes and invoices brought in from a CSV file, as they were.</summary>
public class DocumentImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_Xero_invoice_export_comes_in_with_its_lines_numbers_and_payments()
    {
        const string csv = """
            *ContactName,EmailAddress,*InvoiceNumber,Reference,*InvoiceDate,*DueDate,Total,InvoiceAmountPaid,InvoiceAmountDue,*Description,*Quantity,*UnitAmount,LineAmount,Status
            G3,francois@g3.co.za,INV-0050,Site,2025-03-01,2025-03-08,1700.00,1700.00,0.00,Website care,1,1200.00,1200.00,Paid
            G3,francois@g3.co.za,INV-0050,Site,2025-03-01,2025-03-08,1700.00,1700.00,0.00,Extra changes,2,250.00,500.00,Paid
            "Karoo Coffee Roasters",,INV-0051,,2025-03-05,2025-03-12,900.00,400.00,500.00,Hosting,1,900.00,900.00,Awaiting Payment
            """;

        var plan = DocumentImport.Plan(csv, null, [], Now);

        Assert.Empty(plan.Problems);
        Assert.Equal(2, plan.Invoices);
        var g3 = plan.Documents.Single(d => OwnInvoicing.NumberOf(d) == "INV-0050");
        Assert.Equal(DocumentStatus.Issued, g3.Status);
        Assert.Equal(DocumentOrigin.Import, g3.Document.Origin);
        Assert.Equal(2, g3.Document.Lines.Count);
        Assert.Equal(1700m, g3.Document.Total);
        Assert.Equal(new DateOnly(2025, 3, 1), g3.Document.Date);
        Assert.Equal(Settlement.Paid, g3.Settled?.As);
        Assert.Equal("francois@g3.co.za", g3.Document.Customer.Email);

        var karoo = plan.Documents.Single(d => OwnInvoicing.NumberOf(d) == "INV-0051");
        Assert.Null(karoo.Settled);
        Assert.Equal(500m, Receivables.Outstanding(karoo));
        Assert.Equal("Part paid", OwnInvoicing.Status(karoo));
    }

    [Fact]
    public void A_short_list_of_my_own_with_totals_only_comes_in_and_semicolons_work()
    {
        const string csv = """
            Type;Number;Client;Date;Total;Status
            Quote;QU-0901;G3;18/11/2024;R 12 500,00;Accepted
            Quote;QU-0902;Modics;20/11/2024;4500;Declined
            Invoice;WI-77;Modics;1 Dec 2024;4500;Sent
            """;

        var plan = DocumentImport.Plan(csv, null, [], Now);

        Assert.Empty(plan.Problems);
        Assert.Equal(2, plan.Quotes);
        Assert.Equal(1, plan.Invoices);
        var accepted = plan.Documents.Single(d => OwnInvoicing.NumberOf(d) == "QU-0901");
        Assert.Equal(12500m, accepted.Document.Total);
        Assert.Equal(Settlement.Accepted, accepted.Settled?.As);
        Assert.Equal(new DateOnly(2024, 11, 18), accepted.Document.Date);
        Assert.Single(accepted.Document.Lines);
        Assert.Equal(Settlement.Declined, plan.Documents.Single(d => OwnInvoicing.NumberOf(d) == "QU-0902").Settled?.As);
        var sent = plan.Documents.Single(d => OwnInvoicing.NumberOf(d) == "WI-77");
        Assert.Equal(DocumentStatus.Issued, sent.Status);
        Assert.Equal(4500m, Receivables.Outstanding(sent));
    }

    [Fact]
    public void Numbers_already_here_are_left_out_so_a_file_can_come_in_twice()
    {
        const string csv = "Invoice number,Customer,Total\nINV-0050,G3,100\nINV-0052,G3,200\n";
        var first = DocumentImport.Plan(csv, null, [], Now);

        var again = DocumentImport.Plan(csv, null, first.Documents, Now);

        Assert.Empty(again.Documents);
        Assert.Equal(["INV-0050 is already here.", "INV-0052 is already here."], again.Skipped);
        Assert.Equal("Nothing new to bring in. 2 are here already.", DocumentImport.Summary(again));
    }

    [Fact]
    public void Rows_that_cannot_be_read_say_why()
    {
        const string csv = "Number,Client,Total,Date\n,G3,100,2025-01-01\n12,G3,100,2025-01-01\nINV-9,,100,2025-01-01\nINV-10,G3,100,31/31/2025\n";

        var plan = DocumentImport.Plan(csv, null, [], Now);

        Assert.Empty(plan.Documents);
        Assert.Contains("Row 2 has no number, so it was left out.", plan.Problems);
        Assert.Contains("Row 3 (12) doesn't say whether it's a quote or an invoice, so it was left out.", plan.Problems);
        Assert.Contains("INV-9 (row 4) has no client, so it was left out.", plan.Problems);
        Assert.Contains(plan.Problems, p => p.StartsWith("INV-10 has a date that couldn't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_number_or_client_column_nothing_comes_in()
    {
        Assert.Equal(["The file needs a column with the quote or invoice number, headed Number, Invoice number or Quote number."],
            DocumentImport.Plan("Client,Total\nG3,100\n", DocumentKind.Invoice, [], Now).Problems);
        Assert.Equal(["The file needs a column with the client, headed Client, Customer or Contact name."],
            DocumentImport.Plan("Number,Total\n1,100\n", DocumentKind.Invoice, [], Now).Problems);
    }

    [Fact]
    public void Brought_in_ones_count_for_numbering_and_can_be_deleted()
    {
        var plan = DocumentImport.Plan("Invoice number,Customer,Total,Status\nINV-0090,G3,100,Paid\n", null, [], Now);

        Assert.Equal("INV-0091", OwnInvoicing.NextNumber(DocumentKind.Invoice, plan.Documents));
        Assert.True(Approvals.CanDelete(plan.Documents[0]));
        Assert.False(Approvals.AssistantMayChange(plan.Documents[0]));
    }
}
