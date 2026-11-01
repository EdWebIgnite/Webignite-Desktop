using System.Text.Json.Nodes;
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
}
