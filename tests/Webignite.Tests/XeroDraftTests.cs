using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Xero;
using Webignite.Xero;
using Webignite.Xero.Auth;

namespace Webignite.Tests;

public sealed class XeroDraftTests
{
    private static readonly XeroContact Karoo = new("k1", "Karoo Coffee Roasters", "accounts@karoocoffee.co.za", true);

    [Fact]
    public void Contacts_match_on_name_and_email_and_doubt_goes_to_a_person()
    {
        XeroContact[] contacts = [Karoo, new("f1", "Fynbos Physio", null, true), new("o1", "Old Client", null, false)];

        Assert.Equal(Karoo, Assert.IsType<ContactMatch.Use>(XeroRules.MatchContact(" karoo  coffee roasters", null, contacts)).Contact);
        Assert.IsType<ContactMatch.Use>(XeroRules.MatchContact("Karoo Coffee Roasters", "ACCOUNTS@karoocoffee.co.za", contacts));
        Assert.IsType<ContactMatch.Choose>(XeroRules.MatchContact("Karoo Coffee", "accounts@karoocoffee.co.za", contacts));
        Assert.IsType<ContactMatch.Choose>(XeroRules.MatchContact("Karoo Coffee Roasters", "other@karoo.co.za", contacts));
        Assert.IsType<ContactMatch.New>(XeroRules.MatchContact("Bosveld Builders", null, contacts));
        Assert.IsType<ContactMatch.Archived>(XeroRules.MatchContact("Old Client", null, contacts));
        Assert.IsType<ContactMatch.Choose>(XeroRules.MatchContact("Fynbos Physio", null, [.. contacts, new("f2", "FYNBOS PHYSIO", null, true)]));
    }

    [Fact]
    public void Duplicates_and_our_own_draft_are_found_by_customer_date_and_total()
    {
        var doc = Samples.Completed(Samples.NewInvoice());
        var same = new XeroExisting("x1", "INV-0110", "k1", doc.Date, "ZAR", 450m, "DRAFT");
        Assert.NotNull(XeroRules.Duplicate(doc, "k1", [same], []));
        Assert.Null(XeroRules.Duplicate(doc, "k1", [same with { Status = "VOIDED" }], []));
        Assert.Null(XeroRules.Duplicate(doc, "k1", [same with { Total = 451m }], []));
        Assert.Null(XeroRules.Duplicate(doc, "k1", [same], ["x1"]));

        Assert.Equal(same, XeroRules.FindOurs(doc, "k1", [same], []));
        Assert.Null(XeroRules.FindOurs(doc, "k1", [same, same with { Id = "x2" }], []));
        Assert.Null(XeroRules.FindOurs(doc, "k1", [same with { Status = "AUTHORISED" }], []));
    }

    [Fact]
    public async Task A_draft_is_saved_before_writing_created_once_and_checked()
    {
        var xero = new FakeXero();
        var (drafts, saves) = Make(xero);
        var stored = Stored();

        var result = await drafts.CreateAsync(stored, new XeroTarget("200"), cancel: TestContext.Current.CancellationToken);

        var done = Assert.IsType<XeroDrafts.Result.Done>(result);
        Assert.Equal(XeroWriteState.Verified, done.Stored.XeroState);
        Assert.Equal("INV-0111", done.Stored.XeroNumber);
        Assert.Equal(Webignite.Core.Documents.DocumentStatus.InXero, done.Stored.Status);
        Assert.Equal(XeroWriteState.Writing, saves[0].XeroState);
        Assert.NotNull(saves[0].XeroSent);
        var put = Assert.Single(xero.Puts);
        Assert.Equal(stored.Id + "-document", put.Key);
        Assert.Equal("DRAFT", put.Body["Invoices"]![0]!["Status"]!.GetValue<string>());
        Assert.Equal("NONE", put.Body["Invoices"]![0]!["LineItems"]![0]!["TaxType"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_new_customer_needs_a_yes_before_a_contact_is_made()
    {
        var xero = new FakeXero();
        var (drafts, _) = Make(xero);
        var stored = Stored("Bosveld Builders");

        var asked = await drafts.CreateAsync(stored, new XeroTarget("200"), cancel: TestContext.Current.CancellationToken);
        Assert.IsType<ContactMatch.New>(Assert.IsType<XeroDrafts.Result.NeedsContact>(asked).Match);
        Assert.Empty(xero.Puts);

        var done = await drafts.CreateAsync(stored, new XeroTarget("200"), createContact: true, customerEmail: "pieter@bosveld.co.za",
            cancel: TestContext.Current.CancellationToken);
        Assert.IsType<XeroDrafts.Result.Done>(done);
        Assert.Equal(["Contacts", "Invoices?unitdp=4"], xero.Puts.Select(p => p.Path));
        Assert.Equal("pieter@bosveld.co.za", xero.Puts[0].Body["Contacts"]![0]!["EmailAddress"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_broken_write_is_not_retried_and_check_finds_the_draft()
    {
        var xero = new FakeXero { DropFirstWrite = true };
        var (drafts, _) = Make(xero);

        var result = await drafts.CreateAsync(Stored(), new XeroTarget("200"), cancel: TestContext.Current.CancellationToken);
        var stopped = Assert.IsType<XeroDrafts.Result.Stopped>(result);
        Assert.Equal(XeroWriteState.Uncertain, stopped.Stored.XeroState);
        Assert.Single(xero.Puts);

        // Trying again is refused; Check Xero finds the one that did arrive.
        Assert.IsType<XeroDrafts.Result.Stopped>(await drafts.CreateAsync(stopped.Stored, new XeroTarget("200"), cancel: TestContext.Current.CancellationToken));
        Assert.Single(xero.Puts);
        var check = await drafts.CheckAsync(stopped.Stored, TestContext.Current.CancellationToken);
        Assert.Equal(XeroWriteState.Verified, Assert.IsType<XeroDrafts.Result.Done>(check).Stored.XeroState);
    }

    [Fact]
    public async Task Same_customer_date_and_total_already_in_Xero_stops_it()
    {
        var xero = new FakeXero();
        var (drafts, _) = Make(xero);
        Assert.IsType<XeroDrafts.Result.Done>(await drafts.CreateAsync(Stored(), new XeroTarget("200"), cancel: TestContext.Current.CancellationToken));
        var again = await drafts.CreateAsync(Stored(), new XeroTarget("200"), cancel: TestContext.Current.CancellationToken);
        Assert.Contains("already has INV-0111", Assert.IsType<XeroDrafts.Result.Stopped>(again).Reason);
        Assert.Single(xero.Puts);
    }

    private static Webignite.Core.Documents.StoredDocument Stored(string customer = "Karoo Coffee Roasters") => new()
    {
        Id = Webignite.Core.Documents.StoredDocument.NewId(),
        Document = Samples.Completed(Samples.NewInvoice() with { Customer = new Webignite.Core.Documents.Customer { Name = customer } }),
    };

    private static (XeroDrafts Drafts, List<Webignite.Core.Documents.StoredDocument> Saves) Make(FakeXero xero)
    {
        var tokens = new MemoryTokenStore();
        tokens.WriteRefreshToken("refresh");
        var http = new HttpClient(xero);
        var api = new XeroApi(new XeroSession(new XeroOptions { ClientId = "abc" }, http, tokens), http, "t1");
        var saves = new List<Webignite.Core.Documents.StoredDocument>();
        return (new XeroDrafts(api, s => { saves.Add(s); return s; }, () => []), saves);
    }

    /// <summary>Enough of Xero to create and read back drafts.</summary>
    private sealed class FakeXero : HttpMessageHandler
    {
        public List<(string Path, string Key, JsonObject Body)> Puts { get; } = [];
        public bool DropFirstWrite { get; init; }
        private readonly List<JsonObject> invoices = [];
        private readonly List<JsonObject> contacts = [JsonNode.Parse("""{"ContactID":"k1","Name":"Karoo Coffee Roasters","EmailAddress":"accounts@karoocoffee.co.za","ContactStatus":"ACTIVE"}""")!.AsObject()];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "identity.xero.com")
                return Json("""{"access_token":"a","refresh_token":"r","expires_in":1800}""");
            Assert.Equal("t1", request.Headers.GetValues("xero-tenant-id").Single());
            var path = uri.PathAndQuery["/api.xro/2.0/".Length..];

            if (request.Method == HttpMethod.Put)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancel))!.AsObject();
                Puts.Add((path, request.Headers.GetValues("Idempotency-Key").Single(), body));
                if (path == "Contacts")
                {
                    var c = body["Contacts"]![0]!.AsObject().DeepClone().AsObject();
                    c["ContactID"] = "new1";
                    c["ContactStatus"] = "ACTIVE";
                    contacts.Add(c);
                    return Json(new JsonObject { ["Contacts"] = new JsonArray(c.DeepClone()) }.ToJsonString());
                }
                var inv = body["Invoices"]![0]!.AsObject().DeepClone().AsObject();
                inv["InvoiceID"] = "inv" + (invoices.Count + 1);
                inv["InvoiceNumber"] = "INV-" + (111 + invoices.Count).ToString("0000");
                inv["DateString"] = inv["Date"]!.GetValue<string>() + "T00:00:00";
                decimal total = 0;
                foreach (var line in inv["LineItems"]!.AsArray().OfType<JsonObject>())
                    if (line["Quantity"] is { } q)
                    {
                        line["LineAmount"] = q.GetValue<decimal>() * line["UnitAmount"]!.GetValue<decimal>();
                        total += line["LineAmount"]!.GetValue<decimal>();
                    }
                inv["SubTotal"] = total;
                inv["TotalTax"] = 0m;
                inv["Total"] = total;
                invoices.Add(inv);
                if (DropFirstWrite && invoices.Count == 1)
                    throw new HttpRequestException("connection reset");
                return Json(new JsonObject { ["Invoices"] = new JsonArray(inv.DeepClone()) }.ToJsonString());
            }

            if (path.StartsWith("Contacts?page=1", StringComparison.Ordinal))
                return Json(new JsonObject { ["Contacts"] = new JsonArray([.. contacts.Select(c => c.DeepClone())]) }.ToJsonString());
            if (path.StartsWith("Invoices?page=1", StringComparison.Ordinal))
            {
                var contact = System.Web.HttpUtility.ParseQueryString(uri.Query)["ContactIDs"];
                var rows = invoices.Where(i => i["Contact"]!["ContactID"]!.GetValue<string>() == contact).Select(i => i.DeepClone());
                return Json(new JsonObject { ["Invoices"] = new JsonArray([.. rows]) }.ToJsonString());
            }
            if (path.StartsWith("Invoices/", StringComparison.Ordinal))
            {
                var id = path["Invoices/".Length..].Split('?')[0];
                return Json(new JsonObject { ["Invoices"] = new JsonArray(invoices.Single(i => i["InvoiceID"]!.GetValue<string>() == id).DeepClone()) }.ToJsonString());
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
