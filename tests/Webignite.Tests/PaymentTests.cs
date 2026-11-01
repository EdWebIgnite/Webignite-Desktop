using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Calendar;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;
using Webignite.Xero;
using Webignite.Xero.Auth;

namespace Webignite.Tests;

public sealed class PaymentTests
{
    private static readonly DateOnly Today = Samples.Today;
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 10, 0, 0, TimeSpan.FromHours(2));

    private static StoredDocument Invoice(string id, decimal total, int dueInDays) => new()
    {
        Id = id,
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Work", 1, total))) with { DueOrExpiryDate = Today.AddDays(dueInDays) },
        Status = DocumentStatus.InXero,
        XeroId = "x-" + id,
        XeroNumber = "INV-" + id,
    };

    private static StoredDocument Quote(string id, decimal total) => Invoice(id, total, 10) with
    {
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Site", 1, total))) with { Kind = DocumentKind.Quote, DueOrExpiryDate = Today.AddDays(10) },
    };

    [Fact]
    public void Before_Xero_is_read_the_whole_total_is_owed()
    {
        StoredDocument[] docs = [Invoice("1", 1000m, -5), Invoice("2", 500m, 10), Invoice("3", 200m, -1) with { Status = DocumentStatus.Draft }];
        Assert.Equal(1500m, Receivables.Owed(docs));
        Assert.Equal(1000m, Receivables.Overdue(docs, Today));
    }

    [Fact]
    public void Part_payments_reduce_what_is_owed_and_paid_invoices_drop_out()
    {
        var part = Receivables.Apply(Invoice("1", 1000m, -5), new XeroStanding("AUTHORISED", 400m, 600m, null), Now).Changed!;
        var paid = Receivables.Apply(Invoice("2", 500m, -5), new XeroStanding("PAID", 0m, 500m, Today), Now).Changed!;
        Assert.Equal(400m, Receivables.Owed([part, paid]));
        Assert.Equal(400m, Receivables.Overdue([part, paid], Today));
        Assert.Equal("Part paid", Receivables.Standing(part));
        Assert.Equal("Paid", Receivables.Standing(paid));
        Assert.Equal("paid", paid.SettledAs);
        Assert.Equal(Today, paid.PaidOn);
        Assert.False(Receivables.NeedsChecking(paid));
    }

    [Fact]
    public void Drafts_waiting_in_Xero_are_not_owed_yet()
    {
        var draft = Receivables.Apply(Invoice("1", 1000m, -5), new XeroStanding("DRAFT", 1000m, 0m, null), Now).Changed!;
        Assert.Equal(0m, Receivables.Owed([draft]));
        Assert.Null(draft.Settled);
        Assert.Equal("Draft in Xero", Receivables.Standing(draft));
        Assert.Empty(Chasers.Find([draft], Today));
    }

    [Fact]
    public void Voided_invoices_and_settled_quotes_stop_counting()
    {
        var voided = Receivables.Apply(Invoice("1", 1000m, -5), new XeroStanding("VOIDED", 0m, 0m, null), Now).Changed!;
        var accepted = Receivables.Apply(Quote("2", 3000m), new XeroStanding("ACCEPTED", null, null, null), Now).Changed!;
        var open = Receivables.Apply(Quote("3", 2000m), new XeroStanding("SENT", null, null, null), Now).Changed!;
        Assert.Equal(0m, Receivables.Owed([voided]));
        Assert.Equal(2000m, Receivables.QuotesOut([accepted, open], Today));
        Assert.Equal("Accepted", Receivables.Standing(accepted));
    }

    [Fact]
    public void Payments_are_news_only_after_the_first_look()
    {
        var (first, news) = Receivables.Apply(Invoice("1", 1000m, -5), new XeroStanding("AUTHORISED", 400m, 600m, null), Now);
        Assert.Null(news);
        Assert.Equal(Now, first!.XeroChecked);

        var (same, none) = Receivables.Apply(first, new XeroStanding("AUTHORISED", 400m, 600m, null), Now.AddMinutes(30));
        Assert.Null(same);
        Assert.Null(none);

        var (paid, payment) = Receivables.Apply(first, new XeroStanding("PAID", 0m, 1000m, Today), Now.AddHours(1));
        Assert.Equal(400m, payment!.Amount);
        Assert.True(payment.PaidInFull);
        Assert.Equal("Karoo Coffee Roasters paid INV-1", payment.Title);
        Assert.Equal("R 400.00 came in. Nothing more is owed on it.", payment.Text);
        Assert.NotNull(paid!.Settled);
    }

    [Fact]
    public void A_part_payment_says_what_is_still_owed()
    {
        var first = Receivables.Apply(Invoice("1", 1000m, -5), new XeroStanding("AUTHORISED", 1000m, 0m, null), Now).Changed!;
        var (_, payment) = Receivables.Apply(first, new XeroStanding("AUTHORISED", 750m, 250m, null), Now.AddHours(1));
        Assert.False(payment!.PaidInFull);
        Assert.Equal("R 250.00 came in for INV-1. R 750.00 is still owed.", payment.Text);
    }

    [Fact]
    public void Paid_invoices_leave_the_calendar_and_to_dos_and_chasing_uses_what_is_owed()
    {
        var part = Receivables.Apply(Invoice("1", 1000m, -5), new XeroStanding("AUTHORISED", 400m, 600m, null), Now).Changed!;
        var paid = Receivables.Apply(Invoice("2", 500m, -5), new XeroStanding("PAID", 0m, 500m, Today), Now).Changed!;

        var tasks = AutomaticTasks.Suggest([part, paid], new HashSet<string>(), new TaskSettings(), Today, Now);
        Assert.Equal(["Chase overdue INV-1 for Karoo Coffee Roasters, R 400.00"], tasks.Select(t => t.Title));
        Assert.Equal(["INV-1"], Chasers.Find([part, paid], Today).Select(c => c.Number));
        Assert.Single(Agenda.Build(Today.AddDays(-5), Today.AddDays(-5), [], [], [part, paid]));
    }

    [Fact]
    public async Task Xero_is_read_by_invoice_ids_and_quotes_one_by_one()
    {
        var xero = new FakeXero();
        var tokens = new MemoryTokenStore();
        tokens.WriteRefreshToken("refresh");
        var http = new HttpClient(xero);
        var standings = new XeroStandings(new XeroApi(new XeroSession(new XeroOptions { ClientId = "abc" }, http, tokens), http, "t1"));

        var ids = Enumerable.Range(1, 45).Select(i => "i" + i).ToList();
        var invoices = await standings.InvoicesAsync(ids, TestContext.Current.CancellationToken);
        Assert.Equal(2, xero.InvoiceCalls);
        Assert.Equal(new XeroStanding("PAID", 0m, 100m, new DateOnly(2026, 10, 30)), invoices["i1"]);
        Assert.Equal(new XeroStanding("AUTHORISED", 100m, 0m, null), invoices["i2"]);

        var quotes = await standings.QuotesAsync(["q1"], TestContext.Current.CancellationToken);
        Assert.Equal("ACCEPTED", quotes["q1"].Status);
        Assert.Empty(xero.Writes);
    }

    private sealed class FakeXero : HttpMessageHandler
    {
        public int InvoiceCalls { get; private set; }
        public List<string> Writes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "identity.xero.com")
                return Task.FromResult(Json("""{"access_token":"a","refresh_token":"r","expires_in":1800}"""));
            if (request.Method != HttpMethod.Get)
                Writes.Add(uri.ToString());
            var path = uri.PathAndQuery["/api.xro/2.0/".Length..];
            if (path.StartsWith("Invoices?", StringComparison.Ordinal))
            {
                InvoiceCalls++;
                var ids = System.Web.HttpUtility.ParseQueryString(uri.Query)["IDs"]!.Split(',');
                Assert.True(ids.Length <= XeroStandings.IdsPerRequest);
                var rows = new JsonArray();
                foreach (var id in ids)
                    rows.Add(id == "i1"
                        ? JsonNode.Parse($$"""{"InvoiceID":"{{id}}","Status":"PAID","AmountDue":0,"AmountPaid":100,"FullyPaidOnDate":"/Date(1793318400000+0000)/"}""")
                        : JsonNode.Parse($$"""{"InvoiceID":"{{id}}","Status":"AUTHORISED","AmountDue":100,"AmountPaid":0}"""));
                return Task.FromResult(Json(new JsonObject { ["Invoices"] = rows }.ToJsonString()));
            }
            if (path == "Quotes/q1")
                return Task.FromResult(Json("""{"Quotes":[{"QuoteID":"q1","Status":"ACCEPTED"}]}"""));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
