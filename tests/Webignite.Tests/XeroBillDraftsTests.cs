using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Finance;
using Webignite.Xero;
using Webignite.Xero.Auth;

namespace Webignite.Tests;

/// <summary>A supplier's bill put into Xero as a draft, with their PDF attached.</summary>
public sealed class XeroBillDraftsTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly BillDraft Bill = new()
    {
        Supplier = "xneelo", Number = "INV-7781", Date = new DateOnly(2026, 11, 1), Due = new DateOnly(2026, 11, 8), Total = 1899m,
        Description = "Bill INV-7781 from xneelo",
    };

    [Fact]
    public async Task A_known_supplier_gets_a_draft_bill_with_the_pdf_attached()
    {
        var xero = new FakeXero();
        var result = await Make(xero).DraftAsync(Bill, "441", [1, 2, 3], "xneelo November.pdf", addSupplier: false, Cancel);

        var done = Assert.IsType<XeroBillDrafts.Result.Done>(result);
        Assert.Null(done.Problem);
        var bill = xero.Puts.Single(p => p.Path == "Invoices").Body!["Invoices"]![0]!;
        Assert.Equal(("ACCPAY", "DRAFT", "s1", "INV-7781", "2026-11-08"),
            (bill["Type"]!.GetValue<string>(), bill["Status"]!.GetValue<string>(), bill["Contact"]!["ContactID"]!.GetValue<string>(),
             bill["InvoiceNumber"]!.GetValue<string>(), bill["DueDate"]!.GetValue<string>()));
        var line = bill["LineItems"]![0]!;
        Assert.Equal(("441", "NONE", 1899m), (line["AccountCode"]!.GetValue<string>(), line["TaxType"]!.GetValue<string>(), line["UnitAmount"]!.GetValue<decimal>()));
        var attached = Assert.Single(xero.Puts, p => p.Path.Contains("/Attachments/", StringComparison.Ordinal));
        Assert.Equal("Invoices/bill1/Attachments/xneelo%20November.pdf", attached.Path);
        Assert.Equal("application/pdf", attached.MediaType);
    }

    [Fact]
    public async Task A_new_supplier_is_only_added_when_asked()
    {
        var xero = new FakeXero();
        var bill = Bill with { Supplier = "Printing.co.za" };
        var asked = Assert.IsType<XeroBillDrafts.Result.NeedsSupplier>(await Make(xero).DraftAsync(bill, "441", null, null, addSupplier: false, Cancel));
        Assert.Contains("isn't in Xero yet", asked.Question, StringComparison.Ordinal);
        Assert.Empty(xero.Puts);

        Assert.IsType<XeroBillDrafts.Result.Done>(await Make(xero).DraftAsync(bill, "441", null, null, addSupplier: true, Cancel));
        Assert.Equal(["Contacts", "Invoices"], xero.Puts.Select(p => p.Path));
    }

    [Fact]
    public async Task A_bill_that_doesnt_check_out_isnt_sent()
    {
        var xero = new FakeXero();
        await Assert.ThrowsAsync<XeroException>(() => Make(xero).DraftAsync(Bill with { Total = null }, "441", null, null, false, Cancel));
        Assert.Empty(xero.Puts);
    }

    private static XeroBillDrafts Make(FakeXero xero)
    {
        var tokens = new MemoryTokenStore();
        tokens.WriteRefreshToken("refresh");
        var http = new HttpClient(xero);
        return new XeroBillDrafts(new XeroApi(new XeroSession(new XeroOptions { ClientId = "abc" }, http, tokens), http, "t1"));
    }

    private sealed class FakeXero : HttpMessageHandler
    {
        public List<(string Path, JsonObject? Body, string? MediaType)> Puts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "identity.xero.com")
                return Json("""{"access_token":"a","refresh_token":"r","expires_in":1800}""");
            var path = uri.AbsolutePath["/api.xro/2.0/".Length..];
            if (request.Method == HttpMethod.Put)
            {
                if (path.Contains("/Attachments/", StringComparison.Ordinal))
                {
                    Puts.Add((uri.PathAndQuery["/api.xro/2.0/".Length..], null, request.Content!.Headers.ContentType?.MediaType));
                    return Json("{}");
                }
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancel))!.AsObject();
                Puts.Add((path, body, null));
                return path == "Contacts"
                    ? Json("""{"Contacts":[{"ContactID":"s2"}]}""")
                    : Json("""{"Invoices":[{"InvoiceID":"bill1"}]}""");
            }
            return path == "Contacts"
                ? Json("""{"Contacts":[{"ContactID":"s1","Name":"xneelo","ContactStatus":"ACTIVE"},{"ContactID":"k1","Name":"Karoo Coffee Roasters","ContactStatus":"ACTIVE"}]}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
