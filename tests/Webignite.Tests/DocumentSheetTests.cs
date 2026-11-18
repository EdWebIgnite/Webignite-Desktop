using Webignite.Core.Clients;
using Webignite.Core.Documents;

namespace Webignite.Tests;

public class DocumentSheetTests
{
    private static readonly MailBrand Brand = new() { CompanyName = "Webignite", Address = "1 Long Street\nCape Town", BankDetails = "FNB\nAccount 123", PayFastMerchantId = "10012345" };

    private static StoredDocument Invoice(string? number = "INV-0112", decimal? paid = null) => new()
    {
        Id = "d",
        Xero = new XeroLink { Number = number, AmountPaid = paid },
        Document = new Document
        {
            Kind = DocumentKind.Invoice,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = "Francois Booysens" },
            Date = new DateOnly(2026, 11, 18),
            DueOrExpiryDate = new DateOnly(2026, 11, 25),
            Lines = [new Line { Description = "Website care", Quantity = 2, UnitPrice = 1250 }, new Line { Description = "For November" }],
            Total = 2500,
        },
    };

    [Fact]
    public void A_client_known_by_a_company_is_addressed_as_the_company_attention_the_person()
    {
        var g3 = new Client { Id = "c", Name = "Francois Booysens", Company = "G3", Address = "2 Main Road" };

        var sheet = DocumentSheets.Build(Invoice(), Brand, g3);

        Assert.Equal(["G3", "Attention: Francois Booysens", "2 Main Road"], sheet.To);
    }

    [Fact]
    public void Without_a_company_it_is_addressed_to_the_customer()
    {
        Assert.Equal(["Francois Booysens"], DocumentSheets.Build(Invoice(), Brand, null).To);
    }

    [Fact]
    public void An_invoice_shows_dates_lines_total_bank_and_the_pay_link()
    {
        var sheet = DocumentSheets.Build(Invoice(), Brand, null);

        Assert.Equal("INVOICE", sheet.Title);
        Assert.Equal("Invoice INV-0112.pdf", sheet.FileName);
        Assert.Equal([new("Date", "18 November 2026"), new("Due", "25 November 2026")], sheet.Facts);
        Assert.Equal(new SheetRow("Website care", "2", Money.Rand(1250), Money.Rand(2500)), sheet.Rows[0]);
        Assert.Null(sheet.Rows[1].Amount);
        Assert.Equal([new SheetTotal("Total", Money.Rand(2500), true)], sheet.Totals);
        Assert.Equal(["FNB", "Account 123"], sheet.Bank);
        Assert.Equal("https://www.payfast.co.za/eng/process?cmd=_paynow&receiver=10012345&item_name=Invoice%20INV-0112&m_payment_id=INV-0112&amount=2500.00", sheet.PayLink);
    }

    [Fact]
    public void A_part_paid_invoice_asks_for_what_is_left()
    {
        var sheet = DocumentSheets.Build(Invoice(paid: 1000), Brand, null);

        Assert.Equal(new SheetTotal("Amount due", Money.Rand(1500), true), sheet.Totals[^1]);
        Assert.EndsWith("&amount=1500.00", sheet.PayLink);
    }

    [Fact]
    public void A_quote_has_no_banking_details_or_pay_link_and_says_valid_until()
    {
        var invoice = Invoice("QU-1024");
        var quote = invoice with { Document = invoice.Document with { Kind = DocumentKind.Quote } };

        var sheet = DocumentSheets.Build(quote, Brand, null);

        Assert.Equal("QUOTE", sheet.Title);
        Assert.Empty(sheet.Bank);
        Assert.Null(sheet.PayLink);
        Assert.Equal("Valid until", sheet.Facts[1].Label);
    }

    [Fact]
    public void No_number_yet_is_a_draft_without_a_pay_link()
    {
        var sheet = DocumentSheets.Build(Invoice(number: null), Brand, null);

        Assert.True(sheet.IsDraft);
        Assert.Null(sheet.PayLink);
    }

    [Fact]
    public void No_merchant_id_means_no_pay_link()
    {
        Assert.Null(PayFast.Link("", 100, "Invoice 1", "1"));
        Assert.Null(PayFast.Link("10012345", 0, "Invoice 1", "1"));
    }
}
