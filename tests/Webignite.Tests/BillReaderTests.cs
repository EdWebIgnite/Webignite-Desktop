using Webignite.Core.Finance;

namespace Webignite.Tests;

/// <summary>Reading a supplier's PDF bill into boxes to check.</summary>
public sealed class BillReaderTests
{
    private const string Xneelo = """
        xneelo (Pty) Ltd
        Tax Invoice
        Invoice Number: INV-7781
        Invoice Date: 01/11/2026
        Due Date: 08/11/2026
        Bill to: Webignite (Pty) Ltd
        Dedicated server, November        1 899.00
        Sub Total                         1 899.00
        Amount Due                    R 1 899.00
        """;

    [Fact]
    public void A_typical_bill_is_read()
    {
        var bill = BillReader.Read(Xneelo, ["xneelo", "Adobe"]);
        Assert.Equal("xneelo", bill.Supplier);
        Assert.Equal("INV-7781", bill.Number);
        Assert.Equal(new DateOnly(2026, 11, 1), bill.Date);
        Assert.Equal(new DateOnly(2026, 11, 8), bill.Due);
        Assert.Equal(1899m, bill.Total);
        Assert.Equal("Bill INV-7781 from xneelo", bill.Description);
        Assert.Null(BillReader.Check(bill));
    }

    [Fact]
    public void A_supplier_xero_doesnt_know_takes_the_first_line_and_word_dates_are_read()
    {
        var bill = BillReader.Read("""
            Printing.co.za
            INVOICE
            Invoice # P-2201
            Date 3 November 2026
            Pay by 17 Nov 2026
            Banners x2      640.00
            Total           640.00
            """, []);
        Assert.Equal("Printing.co.za", bill.Supplier);
        Assert.Equal("P-2201", bill.Number);
        Assert.Equal((new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17)), (bill.Date, bill.Due));
        Assert.Equal(640m, bill.Total);
    }

    [Fact]
    public void Without_a_total_label_the_biggest_amount_is_taken_and_nothing_is_a_problem()
    {
        var bill = BillReader.Read("Adobe Systems\n2026-11-15\nCreative Cloud 1,150.00\nVAT 0.00", ["Adobe Systems Software"]);
        Assert.Equal("Adobe Systems", bill.Supplier);
        Assert.Equal(1150m, bill.Total);
        Assert.Equal(new DateOnly(2026, 11, 15), bill.Date);
        Assert.Null(bill.Number);

        var empty = BillReader.Read("", []);
        Assert.Equal("Type who the bill is from.", BillReader.Check(empty));
        Assert.Equal("Type the amount to pay.", BillReader.Check(empty with { Supplier = "x" }));
        Assert.Equal("The due date is before the bill's date.",
            BillReader.Check(new BillDraft { Supplier = "x", Total = 1, Date = new DateOnly(2026, 11, 9), Due = new DateOnly(2026, 11, 1) }));
    }
}
