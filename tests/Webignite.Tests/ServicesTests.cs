using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Clients;
using Webignite.Core.Tasks;
using Webignite.Core.Web;
using Webignite.Data;
using Webignite.Services;
using Webignite.Xero;
using Webignite.Xero.Auth;

namespace Webignite.Tests;

/// <summary>The background checks, run against fake Xero, fake DNS and a fake website visit.</summary>
public sealed class ServicesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(2));

    public ServicesTests() => store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));

    // Payments

    private static XeroApi Api(HttpMessageHandler handler)
    {
        var tokens = new MemoryTokenStore();
        tokens.WriteRefreshToken("refresh");
        var http = new HttpClient(handler);
        return new XeroApi(new XeroSession(new XeroOptions { ClientId = "abc" }, http, tokens), http, "t1");
    }

    [Fact]
    public async Task A_paid_invoice_is_saved_as_paid_announced_and_its_chase_to_do_ticked_off()
    {
        var invoice = store.Save(new StoredDocument
        {
            Id = "inv1",
            Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Work", 1, 1000m))),
            Status = DocumentStatus.InXero,
            XeroId = "x1",
            XeroNumber = "INV-0001",
            // Xero was read once before and nothing was paid, so this payment is news.
            XeroChecked = Now.AddHours(-1),
            XeroAmountPaid = 0m,
            XeroAmountDue = 1000m,
        });
        store.SaveTask(new TaskItem { Id = "t1", Title = "Chase INV-0001", DocumentId = invoice.Id, AutoKey = "invoice-overdue:inv1" });
        var xero = new FakeXero("""{"InvoiceID":"x1","Status":"PAID","AmountDue":0,"AmountPaid":1000,"FullyPaidOnDate":"/Date(1762128000000+0000)/"}""");
        var watch = new PaymentWatch(store, () => Api(xero), new FixedClock(Now));

        var (news, problem) = await watch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Null(problem);
        var payment = Assert.Single(news);
        Assert.Equal(1000m, payment.Amount);
        Assert.True(payment.PaidInFull);
        Assert.Equal(Settlement.Paid, store.Get("inv1")!.SettledAs);
        Assert.True(store.ListTasks().Single(t => t.Id == "t1").Done);
        Assert.Equal(Now, watch.LastRun);
        Assert.False(watch.Due(Now.AddMinutes(10)));
        Assert.True(watch.Due(Now.AddMinutes(31)));
    }

    [Fact]
    public async Task Without_Xero_the_check_says_so_and_changes_nothing()
    {
        var watch = new PaymentWatch(store, () => null, new FixedClock(Now));
        var (news, problem) = await watch.RunAsync(TestContext.Current.CancellationToken);
        Assert.Empty(news);
        Assert.Equal("Connect Xero in Settings first.", problem);
        Assert.Null(watch.LastRun);
    }

    [Fact]
    public async Task A_Xero_outage_is_a_problem_to_show_not_a_crash()
    {
        store.Save(new StoredDocument { Id = "inv1", Document = Samples.Completed(Samples.NewInvoice()), Status = DocumentStatus.InXero, XeroId = "x1" });
        var watch = new PaymentWatch(store, () => Api(new FakeXero(null)), new FixedClock(Now));
        var (_, problem) = await watch.RunAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("Couldn't read payments from Xero", problem);
        Assert.Equal(Now, watch.LastTry);
        Assert.Null(watch.LastRun);
    }

    private sealed class FakeXero(string? invoiceJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (request.RequestUri!.Host == "identity.xero.com")
                return Task.FromResult(Json("""{"access_token":"a","refresh_token":"r","expires_in":1800}"""));
            if (invoiceJson is null)
                throw new HttpRequestException("connection reset");
            return Task.FromResult(Json(new JsonObject { ["Invoices"] = new JsonArray(JsonNode.Parse(invoiceJson)) }.ToJsonString()));
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    // Sites

    [Fact]
    public async Task Two_failed_visits_announce_a_site_down_and_the_result_is_saved()
    {
        store.SaveClient(new Client { Id = "k", Name = "Karoo Coffee", Website = "https://karoocoffee.co.za" });
        var settings = new UptimeSettings();
        var visits = 0;
        var watch = new UptimeWatch(store.ListClients, () => settings, s => settings = s,
            (domain, _) => { visits++; return Task.FromResult(new UptimePing(Now, false, null, 20_000, null, "The site didn't answer.")); },
            new FixedClock(Now));

        var first = await watch.RunAsync(TestContext.Current.CancellationToken);
        var second = await watch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(first);
        var alert = Assert.Single(second);
        Assert.Equal("karoocoffee.co.za is down", alert.Title);
        Assert.Equal(2, visits);
        Assert.True(Uptime.IsDown(settings.Sites["karoocoffee.co.za"]));
        Assert.Equal(Now, watch.LastRun);
    }

    [Fact]
    public async Task A_site_removed_while_the_visits_ran_is_not_recorded()
    {
        store.SaveClient(new Client { Id = "k", Name = "Karoo Coffee", Website = "karoocoffee.co.za" });
        var settings = new UptimeSettings();
        var watch = new UptimeWatch(store.ListClients, () => settings, s => settings = s,
            (domain, _) =>
            {
                settings = settings with { Off = ["karoocoffee.co.za"] };
                return Task.FromResult(new UptimePing(Now, true, 200, 300));
            },
            new FixedClock(Now));

        var alerts = await watch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(alerts);
        Assert.Empty(settings.Sites);
    }

    // Domain check

    [Fact]
    public async Task Dns_is_asked_over_https_and_the_findings_come_back()
    {
        var dns = new FakeDns();
        var checker = new SiteChecker(new HttpClient(dns));

        var findings = await checker.DomainAsync("karoocoffee.co.za", TestContext.Current.CancellationToken);

        Assert.Contains(dns.Asked, q => q.Contains("name=karoocoffee.co.za&type=MX"));
        Assert.Contains(dns.Asked, q => q.Contains("name=_dmarc.karoocoffee.co.za&type=TXT"));
        Assert.Contains(findings, f => f.Value.Contains("mx1.host-h.net", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_Google_is_busy_the_speed_test_says_so_in_plain_words()
    {
        var checker = new SiteChecker(new HttpClient(new FakeDns { SpeedStatus = HttpStatusCode.TooManyRequests }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => checker.SpeedAsync("https://karoocoffee.co.za", TestContext.Current.CancellationToken));
        Assert.Equal("Google is busy with too many tests. Try again in a minute.", error.Message);
    }

    private sealed class FakeDns : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        public HttpStatusCode SpeedStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "www.googleapis.com")
                return Task.FromResult(new HttpResponseMessage(SpeedStatus) { Content = new StringContent("{}") });
            if (uri.Host == "rdap.org")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            Asked.Add(uri.Query);
            var type = uri.Query.Split("type=")[1];
            var answer = type switch
            {
                "MX" => """{"Status":0,"Answer":[{"name":"karoocoffee.co.za.","type":15,"TTL":300,"data":"10 mx1.host-h.net."}]}""",
                "A" => """{"Status":0,"Answer":[{"name":"karoocoffee.co.za.","type":1,"TTL":300,"data":"196.22.142.10"}]}""",
                "NS" => """{"Status":0,"Answer":[{"name":"karoocoffee.co.za.","type":2,"TTL":300,"data":"ns1.host-h.net."}]}""",
                _ => """{"Status":0}""",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/dns-json") });
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("sast", now.Offset, "SAST", "SAST");
    }

    public void Dispose()
    {
        store.Dispose();
        Directory.Delete(folder, recursive: true);
    }
}
