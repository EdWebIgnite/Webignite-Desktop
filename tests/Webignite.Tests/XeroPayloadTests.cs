using System.Text.Json.Nodes;
using Webignite.Core.Xero;
using Webignite.Xero;

namespace Webignite.Tests;

public class XeroPayloadTests
{
    private static Document Invoice() => Samples.Completed(Samples.NewInvoice(
        Samples.Priced("Hosting", 2, 450m) with { ItemCode = "HOST" },
        new Line { Description = "Thank you" }) with { Notes = "EFT to FNB", DocumentDiscountAmount = 100m, Total = null });

    [Fact]
    public void Invoice_is_a_draft_sales_invoice_without_tax()
    {
        var p = XeroPayload.Build(Invoice(), "contact-1", "200", "NONE");

        Assert.Equal("DRAFT", (string?)p["Status"]);
        Assert.Equal("ACCREC", (string?)p["Type"]);
        Assert.Equal("NoTax", (string?)p["LineAmountTypes"]);
        Assert.Equal("2026-11-07", (string?)p["DueDate"]);
        Assert.False(p.ContainsKey("InvoiceNumber"));
        var lines = p["LineItems"]!.AsArray();
        Assert.Equal("HOST", (string?)lines[0]!["ItemCode"]);
        Assert.False(lines[1]!.AsObject().ContainsKey("Quantity"));
        Assert.Equal(-100m, (decimal)lines[2]!["UnitAmount"]!);
        Assert.Equal("Notes:\nEFT to FNB", (string?)lines[3]!["Description"]);
    }

    [Fact]
    public void Quote_uses_quote_fields()
    {
        var doc = Invoice() with { Kind = DocumentKind.Quote, Terms = "50% deposit" };
        var p = XeroPayload.Build(doc, "contact-1", "200", "NONE", "QU-0042");

        Assert.Equal("NOTAX", (string?)p["LineAmountTypes"]);
        Assert.Equal("QU-0042", (string?)p["QuoteNumber"]);
        Assert.Equal("50% deposit", (string?)p["Terms"]);
        Assert.Equal("EFT to FNB", (string?)p["Summary"]);
        Assert.False(p.ContainsKey("Type"));
        Assert.Equal(3, p["LineItems"]!.AsArray().Count);
    }

    [Fact]
    public void Verify_passes_when_xero_saved_the_same_thing()
    {
        var doc = Invoice();
        var sent = XeroPayload.Build(doc, "contact-1", "200", "NONE");
        Assert.Empty(XeroPayload.Verify(doc, sent, SavedBy(doc, sent)));
    }

    [Fact]
    public void Verify_catches_a_changed_total_and_customer()
    {
        var doc = Invoice();
        var sent = XeroPayload.Build(doc, "contact-1", "200", "NONE");
        var received = SavedBy(doc, sent);
        received["Total"] = 1.23m;
        received["Contact"] = new JsonObject { ["ContactID"] = "someone-else" };

        var errors = XeroPayload.Verify(doc, sent, received);
        Assert.Contains("Total differs from the document.", errors);
        Assert.Contains("Customer differs.", errors);
    }

    [Theory]
    [InlineData("/Date(1793404800000+0000)/", "2026-10-31")]
    [InlineData("2026-10-31T00:00:00", "2026-10-31")]
    public void Normalizes_xero_dates(string input, string expected) =>
        Assert.Equal(expected, XeroPayload.NormalizeDate(input));

    /// <summary>What Xero would return for a faithful save, in Xero's own date format.</summary>
    private static JsonObject SavedBy(Document doc, JsonObject sent)
    {
        var received = JsonNode.Parse(sent.ToJsonString())!.AsObject();
        received["SubTotal"] = doc.NetSubtotal();
        received["TotalTax"] = 0m;
        received["Total"] = doc.Total;
        received["Date"] = "/Date(1793404800000+0000)/";
        received["DueDate"] = "2026-11-07T00:00:00";
        foreach (var line in received["LineItems"]!.AsArray().Select(l => l!.AsObject()).Where(l => l.ContainsKey("Quantity")))
        {
            var amount = (decimal)line["Quantity"]! * (decimal)line["UnitAmount"]!;
            line["LineAmount"] = amount;
        }
        return received;
    }

    [Fact]
    public void A_chosen_branding_theme_goes_on_the_draft_and_none_leaves_xeros_default()
    {
        var themed = XeroPayload.Build(Invoice(), "contact-1", "200", "NONE", brandingThemeId: "theme-payfast");
        Assert.Equal("theme-payfast", themed["BrandingThemeID"]!.GetValue<string>());
        Assert.False(XeroPayload.Build(Invoice(), "contact-1", "200", "NONE").ContainsKey("BrandingThemeID"));
    }

    [Fact]
    public void A_line_linked_to_an_item_keeps_its_description_and_the_discount_line_says_what_it_is()
    {
        var doc = Invoice() with { DocumentDiscountAmount = null, DocumentDiscountPercent = 10m };
        var lines = doc.Lines.ToList();
        lines[0] = lines[0] with { Product = "Hosting", ItemCode = "HOSTING" };
        lines[1] = lines[1] with { Product = "Extra" };
        var p = XeroPayload.Build(doc with { Lines = lines }, "contact-1", "200", "NONE", "WEB-001");
        var items = p["LineItems"]!.AsArray();
        Assert.Equal(lines[0].Description, (string?)items[0]!["Description"]);
        Assert.StartsWith("Extra\n", (string?)items[1]!["Description"], StringComparison.Ordinal);
        Assert.Contains(items, l => (string?)l!["Description"] == "Discount (10%)");
        Assert.Equal("WEB-001", (string?)p["InvoiceNumber"]);
    }

    [Theory]
    [InlineData("10%", 10.0, null)]
    [InlineData("12.5 %", 12.5, null)]
    [InlineData("R 1 000", null, 1000.0)]
    [InlineData("1500.50", null, 1500.5)]
    [InlineData("", null, null)]
    public void A_typed_discount_is_read_as_a_percentage_or_rand(string typed, double? percent, double? amount)
    {
        var read = DocumentDiscount.Read(typed);
        Assert.Null(read.Problem);
        Assert.Equal(percent is null ? null : (decimal)percent, read.Percent);
        Assert.Equal(amount is null ? null : (decimal)amount, read.Amount);
    }

    [Theory]
    [InlineData("150%")]
    [InlineData("0%")]
    [InlineData("lots")]
    [InlineData("-50")]
    public void A_discount_that_makes_no_sense_says_so(string typed) => Assert.NotNull(DocumentDiscount.Read(typed).Problem);

    [Fact]
    public void Items_match_by_name_or_code_and_new_ones_get_a_short_unique_code()
    {
        XeroItem[] items = [new("HOSTING", "Hosting"), new("WordPress Setup", "Something else")];
        Assert.Equal("HOSTING", XeroItems.Match(" hosting ", items)!.Code);
        Assert.Equal("WordPress Setup", XeroItems.Match("wordpress setup", items)!.Code);
        Assert.Null(XeroItems.Match("Content population", items));

        Assert.Equal(new XeroItem("Content population", "Content population"), XeroItems.New("Content population", items));
        var clash = XeroItems.New("WordPress Setup & Configuration", items);
        Assert.Equal("WordPress Setup 2", clash.Code);
        Assert.True(clash.Code.Length <= XeroItems.LongestCode);
        Assert.True(XeroItems.New(new string('x', 80), items).Name.Length <= XeroItems.LongestName);
    }
}
