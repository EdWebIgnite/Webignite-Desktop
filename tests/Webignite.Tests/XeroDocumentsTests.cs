using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Data;
using Webignite.Services;
using Webignite.Xero;

namespace Webignite.Tests;

/// <summary>Quotes and invoices made in Xero itself, brought in so the app sees everything.</summary>
public sealed class XeroDocumentsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-xerodocs-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static XeroSalesDocument Invoice(string id, string status = "AUTHORISED", decimal due = 1500m, bool sent = true) => new()
    {
        Id = id,
        Kind = DocumentKind.Invoice,
        Number = "INV-" + id,
        ContactId = "k1",
        ContactName = "Karoo Coffee Roasters",
        Date = new DateOnly(2026, 10, 1),
        DueOrExpiry = new DateOnly(2026, 10, 31),
        Lines = [new Line { Description = "Website hosting, October", Quantity = 1, UnitPrice = 1500m }],
        Subtotal = 1500m,
        Tax = 0m,
        Total = 1500m,
        Status = status,
        AmountDue = due,
        AmountPaid = 1500m - due,
        SentToContact = sent,
    };

    [Fact]
    public void New_ones_come_in_as_made_in_xero_and_webignites_own_are_left_alone()
    {
        var ours = new StoredDocument
        {
            Id = "mine", Document = Samples.NewInvoice(), Status = DocumentStatus.InXero,
            Xero = new XeroLink { Id = "x-ours", State = XeroWriteState.Verified },
        };
        var plan = XeroDocuments.Plan([ours],
            [Invoice("x-ours"), Invoice("x-new"), Invoice("x-void", "VOIDED", 0), Invoice("x-new")], Now);

        var added = Assert.Single(plan.ToAdd);
        Assert.Empty(plan.ToRefresh);
        Assert.Equal("x-new", added.Xero.Id);
        Assert.Equal(DocumentOrigin.Xero, added.Document.Origin);
        Assert.Equal(DocumentStatus.InXero, added.Status);
        Assert.Equal(XeroWriteState.Verified, added.Xero.State);
        Assert.Equal("INV-x-new", added.Document.Number);
        Assert.Equal(1500m, added.Document.Total);
        Assert.NotNull(added.Delivery.Approved);
        Assert.NotNull(added.Delivery.Emailed); // Xero marked it sent
        Assert.Null(added.Settled);
        Assert.True(Receivables.IsOpenInvoice(added));
        Assert.Equal(1500m, Receivables.Outstanding(added));
    }

    [Fact]
    public void One_of_ours_whose_write_broke_is_not_brought_in_twice()
    {
        var broken = new StoredDocument
        {
            Id = "mine", Status = DocumentStatus.InXero,
            Document = Samples.NewInvoice() with { Customer = new Customer { Name = "Karoo Coffee Roasters" }, Total = 1500m },
            Xero = new XeroLink { State = XeroWriteState.Uncertain },
        };
        Assert.Empty(XeroDocuments.Plan([broken], [Invoice("x-maybe")], Now).ToAdd);
        Assert.Single(XeroDocuments.Plan([broken], [Invoice("x-other") with { Total = 900m }], Now).ToAdd);
    }

    [Fact]
    public void A_paid_invoice_comes_in_settled_and_a_draft_quote_as_not_sent()
    {
        var paid = XeroDocuments.ToStored(Invoice("p", "PAID", 0) with { PaidOn = new DateOnly(2026, 10, 20) }, Now, null);
        Assert.Equal(Settlement.Paid, paid.Settled?.As);
        Assert.Equal(new DateOnly(2026, 10, 20), paid.Xero.PaidOn);

        var quote = XeroDocuments.ToStored(Invoice("q", "DRAFT") with { Kind = DocumentKind.Quote, SentToContact = false }, Now, null);
        Assert.Null(quote.Delivery.Emailed);
        Assert.Null(quote.Delivery.Approved);
        Assert.Null(quote.Settled);
    }

    [Fact]
    public void One_made_in_xero_that_changed_there_is_brought_up_to_date_keeping_its_chasing()
    {
        var made = XeroDocuments.ToStored(Invoice("x1"), Now, null);
        var first = made with { Delivery = made.Delivery with { Chased = Now, ChaseCount = 2 } };
        var changed = Invoice("x1", "PAID", 0);

        var plan = XeroDocuments.Plan([first], [changed], Now.AddHours(4));
        var fresh = Assert.Single(plan.ToRefresh);
        Assert.Equal(first.Id, fresh.Id);
        Assert.Equal(2, fresh.Delivery.ChaseCount);
        Assert.Equal(Settlement.Paid, fresh.Settled?.As);

        Assert.Empty(XeroDocuments.Plan([first], [Invoice("x1")], Now.AddHours(4)).ToRefresh); // nothing changed
    }

    [Fact]
    public void A_refresh_from_xero_keeps_credit_notes_attachments_and_the_quote_link()
    {
        var first = XeroDocuments.ToStored(Invoice("x1"), Now, null) with
        {
            CreditNotes = [new CreditNoteLink { Id = "cn1", Amount = 200m }],
            Attachments = [new XeroAttachment { Name = "Timesheet.pdf" }],
            FromQuote = "q1",
        };
        var fresh = Assert.Single(XeroDocuments.Plan([first], [Invoice("x1", "AUTHORISED", 1300m)], Now.AddHours(4)).ToRefresh);
        Assert.Equal((1, 1, "q1"), (fresh.CreditNotes.Count, fresh.Attachments.Count, fresh.FromQuote));
        Assert.Equal(1300m, InvoiceCorrections.Creditable(fresh));
    }

    [Fact]
    public void Xeros_json_is_read_into_a_document()
    {
        var row = JsonNode.Parse("""
            {"InvoiceID":"i9","InvoiceNumber":"INV-0090","Type":"ACCREC","Status":"AUTHORISED","SentToContact":true,
             "Contact":{"ContactID":"k1","Name":"Karoo Coffee Roasters"},"DateString":"2026-10-01T00:00:00","DueDateString":"2026-10-31T00:00:00",
             "Reference":"October","CurrencyCode":"ZAR","SubTotal":2700,"TotalTax":0,"Total":2700,"AmountDue":2700,"AmountPaid":0,
             "LineItems":[{"Description":"Monthly SEO","Quantity":1,"UnitAmount":3000,"DiscountRate":10,"LineAmount":2700,"ItemCode":"SEO","Item":{"Name":"SEO month"}},
                          {"Description":"Thank you","LineAmount":0}]}
            """)!.AsObject();
        var doc = XeroDocumentReader.ToInvoice(row);
        Assert.Equal("INV-0090", doc.Number);
        Assert.Equal(new DateOnly(2026, 10, 31), doc.DueOrExpiry);
        Assert.True(doc.SentToContact);
        Assert.Equal("SEO month", doc.Lines[0].Product);
        Assert.Equal(10m, doc.Lines[0].DiscountPercent);
        Assert.Equal(2700m, doc.Lines[0].Amount());
        Assert.Null(doc.Lines[1].Quantity); // a text line

        var quote = XeroDocumentReader.ToQuote(JsonNode.Parse("""
            {"QuoteID":"q1","QuoteNumber":"QU-0001","Status":"SENT","Contact":{"ContactID":"k1","Name":"Karoo"},
             "DateString":"2026-10-01T00:00:00","ExpiryDateString":"2026-10-31T00:00:00","Summary":"Thanks","Total":100,"LineItems":[]}
            """)!.AsObject());
        Assert.Equal(DocumentKind.Quote, quote.Kind);
        Assert.Equal("Thanks", quote.Summary);
        Assert.Equal(new DateOnly(2026, 10, 31), quote.DueOrExpiry);
    }

    [Fact]
    public async Task The_first_look_reads_everything_and_later_ones_only_what_changed()
    {
        using var store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        var asked = new List<DateTimeOffset?>();
        IReadOnlyList<XeroSalesDocument> rows = [Invoice("x1"), Invoice("x2", "PAID", 0)];
        var watch = new XeroDocumentWatch(store, (_, _, since) =>
        {
            asked.Add(since);
            return Task.FromResult(rows);
        });

        var first = await watch.RunAsync(cancel: TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Added);
        Assert.Equal("2 quotes and invoices made in Xero came in.", first.Summary);
        Assert.Equal(2, store.List(DocumentKind.Invoice).Count);

        rows = [Invoice("x1", "PAID", 0)];
        var second = await watch.RunAsync(cancel: TestContext.Current.CancellationToken);
        Assert.Equal((0, 1), (second.Added, second.Refreshed));
        Assert.Null(asked[0]);
        Assert.NotNull(asked[1]);
        Assert.All(store.List(DocumentKind.Invoice), d => Assert.Equal(Settlement.Paid, d.Settled?.As));

        var again = await watch.RunAsync(fromScratch: true, cancel: TestContext.Current.CancellationToken);
        Assert.Null(asked[2]);
        Assert.Equal("Nothing new from Xero.", again.Summary);
    }

    [Fact]
    public async Task Without_xero_it_says_to_connect()
    {
        using var store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        var watch = new XeroDocumentWatch(store, () => null);
        Assert.Equal("Connect Xero in Settings first.", (await watch.RunAsync(cancel: TestContext.Current.CancellationToken)).Problem);
    }
}
