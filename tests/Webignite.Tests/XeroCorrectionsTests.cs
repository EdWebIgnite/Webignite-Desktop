using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Xero;
using Webignite.Xero.Auth;

namespace Webignite.Tests;

/// <summary>Voiding an invoice (only on a click) and drafting credit notes against it in Xero.</summary>
public sealed class XeroCorrectionsTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static StoredDocument Approved() => new()
    {
        Id = StoredDocument.NewId(),
        Document = Samples.Completed(Samples.NewInvoice()),
        Status = DocumentStatus.InXero,
        Xero = new() { Id = "inv1", Number = "INV-0111", State = XeroWriteState.Verified, ContactId = "k1", Status = "AUTHORISED" },
        Delivery = new() { Approved = DateTimeOffset.UtcNow, Emailed = DateTimeOffset.UtcNow },
    };

    [Fact]
    public void What_can_be_voided_or_credited()
    {
        var approved = Approved();
        Assert.True(InvoiceCorrections.CanVoid(approved));
        Assert.Equal("Void in Xero", InvoiceCorrections.VoidLabel(approved));
        Assert.True(InvoiceCorrections.CanCredit(approved));

        var draft = approved with { Xero = approved.Xero with { Status = "DRAFT" }, Delivery = new() };
        Assert.Equal("Delete the draft in Xero", InvoiceCorrections.VoidLabel(draft));
        Assert.False(InvoiceCorrections.CanCredit(draft));

        var partPaid = approved with { Xero = approved.Xero with { AmountPaid = 100m } };
        Assert.False(InvoiceCorrections.CanVoid(partPaid));
        Assert.True(InvoiceCorrections.CanCredit(partPaid));

        var voided = approved with { Settled = new SettledState { On = DateTimeOffset.UtcNow, As = Settlement.Voided } };
        Assert.False(InvoiceCorrections.CanVoid(voided));
        Assert.False(InvoiceCorrections.CanCredit(voided));
        Assert.False(InvoiceCorrections.CanVoid(Approved() with { Xero = approved.Xero with { State = XeroWriteState.None } }));

        var total = approved.Document.Total!.Value;
        Assert.Null(InvoiceCorrections.CheckAmount(approved, null));
        Assert.NotNull(InvoiceCorrections.CheckAmount(approved, 0m));
        Assert.Contains("more than", InvoiceCorrections.CheckAmount(approved, total + 1), StringComparison.Ordinal);
        var credited = approved with { CreditNotes = [new CreditNoteLink { Id = "c1", Amount = total }] };
        Assert.False(InvoiceCorrections.CanCredit(credited));
    }

    [Fact]
    public async Task An_approved_invoice_is_voided_and_a_draft_deleted()
    {
        var xero = new FakeXero();
        var corrections = Make(xero);
        var voided = await corrections.VoidAsync(Approved(), Cancel);
        Assert.Equal("VOIDED", Assert.Single(xero.Posts).Body["Status"]!.GetValue<string>());
        Assert.Equal(Settlement.Voided, voided.Settled?.As);
        Assert.Equal(0m, voided.Xero.AmountDue);

        var other = new FakeXero { Status = "DRAFT" };
        var deleted = await Make(other).VoidAsync(Approved() with { Delivery = new() }, Cancel);
        Assert.Equal("DELETED", Assert.Single(other.Posts).Body["Status"]!.GetValue<string>());
        Assert.Equal(Settlement.Deleted, deleted.Settled?.As);
    }

    [Fact]
    public async Task An_invoice_paid_in_xero_meanwhile_is_not_voided()
    {
        var xero = new FakeXero { AmountPaid = 200m };
        var e = await Assert.ThrowsAsync<XeroException>(() => Make(xero).VoidAsync(Approved(), Cancel));
        Assert.Contains("payment or credit", e.Message, StringComparison.Ordinal);
        Assert.Empty(xero.Posts);
    }

    [Fact]
    public async Task A_credit_note_goes_in_as_a_draft_for_all_or_part()
    {
        var xero = new FakeXero();
        var corrections = Make(xero);
        var stored = Approved();

        var part = await corrections.DraftCreditNoteAsync(stored, 200m, "Hosting cancelled for December", new XeroTarget("200"), Cancel);
        var sent = xero.Puts[^1].Body["CreditNotes"]![0]!;
        Assert.Equal("ACCRECCREDIT", sent["Type"]!.GetValue<string>());
        Assert.Equal("DRAFT", sent["Status"]!.GetValue<string>());
        Assert.Equal("INV-0111: Hosting cancelled for December", sent["Reference"]!.GetValue<string>());
        Assert.Equal(200m, Assert.Single(sent["LineItems"]!.AsArray())!["UnitAmount"]!.GetValue<decimal>());
        Assert.Equal(stored.Id + "-credit-1", xero.Puts[^1].Key);
        var note = Assert.Single(part.CreditNotes);
        Assert.Equal(("CN-0001", 200m), (note.Number, note.Amount));

        // The rest: not the invoice's lines again, as some was credited already.
        var rest = await corrections.DraftCreditNoteAsync(part, null, null, new XeroTarget("200"), Cancel);
        var second = xero.Puts[^1].Body["CreditNotes"]![0]!;
        Assert.Equal(part.Document.Total - 200m, Assert.Single(second["LineItems"]!.AsArray())!["UnitAmount"]!.GetValue<decimal>());
        Assert.Equal(stored.Id + "-credit-2", xero.Puts[^1].Key);
        Assert.Equal(2, rest.CreditNotes.Count);
        Assert.False(InvoiceCorrections.CanCredit(rest));
        Assert.Empty(xero.Posts); // nothing approved or allocated
    }

    [Fact]
    public async Task Crediting_all_of_it_copies_the_invoice_lines()
    {
        var xero = new FakeXero();
        var stored = Approved();
        await Make(xero).DraftCreditNoteAsync(stored, null, null, new XeroTarget("200"), Cancel);
        var sent = xero.Puts[^1].Body["CreditNotes"]![0]!;
        Assert.Equal(stored.Document.Lines.Count, sent["LineItems"]!.AsArray().Count);
        Assert.Equal("INV-0111", sent["Reference"]!.GetValue<string>());
    }

    private static XeroCorrections Make(FakeXero xero)
    {
        var tokens = new MemoryTokenStore();
        tokens.WriteRefreshToken("refresh");
        var http = new HttpClient(xero);
        var api = new XeroApi(new XeroSession(new XeroOptions { ClientId = "abc" }, http, tokens), http, "t1");
        return new XeroCorrections(api, (s, change) => change(s));
    }

    private sealed class FakeXero : HttpMessageHandler
    {
        public string Status { get; set; } = "AUTHORISED";
        public decimal AmountPaid { get; init; }
        public List<(string Path, JsonObject Body)> Posts { get; } = [];
        public List<(string Path, string Key, JsonObject Body)> Puts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "identity.xero.com")
                return Json("""{"access_token":"a","refresh_token":"r","expires_in":1800}""");
            var path = uri.PathAndQuery["/api.xro/2.0/".Length..];
            if (request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancel))!.AsObject();
                Posts.Add((path, body));
                Status = body[nameof(Status)]!.GetValue<string>();
                return Json("{}");
            }
            if (request.Method == HttpMethod.Put)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancel))!.AsObject();
                Puts.Add((path, request.Headers.GetValues("Idempotency-Key").Single(), body));
                var note = body["CreditNotes"]![0]!.AsObject().DeepClone().AsObject();
                note["CreditNoteID"] = "cn" + Puts.Count;
                note["CreditNoteNumber"] = "CN-000" + Puts.Count;
                note["Total"] = note["LineItems"]!.AsArray().OfType<JsonObject>()
                    .Where(l => l["Quantity"] is not null).Sum(l => l["Quantity"]!.GetValue<decimal>() * l["UnitAmount"]!.GetValue<decimal>());
                return Json(new JsonObject { ["CreditNotes"] = new JsonArray(note) }.ToJsonString());
            }
            return path.StartsWith("Invoices/inv1", StringComparison.Ordinal)
                ? Json($$$"""{"Invoices":[{"InvoiceID":"inv1","InvoiceNumber":"INV-0111","Status":"{{{Status}}}","AmountPaid":{{{AmountPaid.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"Contact":{"ContactID":"k1"}}]}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
