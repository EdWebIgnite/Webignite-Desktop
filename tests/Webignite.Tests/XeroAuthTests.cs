using System.Net;
using System.Net.Sockets;
using System.Text;
using Webignite.Xero.Auth;

namespace Webignite.Tests;

public class XeroAuthTests
{
    [Fact]
    public void Pkce_challenge_matches_the_rfc_example() =>
        // RFC 7636, appendix B
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Authorize_url_asks_for_drafts_scopes_with_pkce()
    {
        var session = new XeroSession(new XeroOptions { ClientId = "abc" }, new HttpClient(), new MemoryTokenStore());
        var url = session.AuthorizeUrl("state1", "challenge1").ToString();

        Assert.StartsWith("https://login.xero.com/identity/connect/authorize?", url);
        Assert.Contains("client_id=abc", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A47310%2Fcallback", url);
        Assert.Contains("offline_access", Uri.UnescapeDataString(url));
    }

    [Fact]
    public async Task Sign_in_exchanges_the_code_and_saves_the_refresh_token()
    {
        var port = FreePort();
        var xero = new FakeXero();
        var store = new MemoryTokenStore();
        var session = new XeroSession(new XeroOptions { ClientId = "abc", RedirectPort = port }, new HttpClient(xero), store);

        var tenants = await session.SignInAsync(async url =>
        {
            // Play the browser: Xero redirects back with the code and the same state.
            var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"];
            _ = Task.Run(() => new HttpClient().GetAsync($"http://localhost:{port}/callback?code=CODE1&state={state}"));
            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal("refresh-1", store.ReadRefreshToken());
        Assert.Contains("code=CODE1", xero.TokenRequests[0]);
        Assert.Contains("code_verifier=", xero.TokenRequests[0]);
        Assert.Equal("Webignite (Pty) Ltd", Assert.Single(tenants).TenantName);
    }

    [Fact]
    public async Task Refresh_saves_the_new_refresh_token()
    {
        var clock = new ManualClock();
        var xero = new FakeXero();
        var store = new MemoryTokenStore();
        store.WriteRefreshToken("refresh-0");
        var session = new XeroSession(new XeroOptions { ClientId = "abc" }, new HttpClient(xero), store, clock);

        Assert.Equal("access-1", await session.AccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("access-1", await session.AccessTokenAsync(TestContext.Current.CancellationToken));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal("access-2", await session.AccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.Equal("refresh-2", store.ReadRefreshToken());
        Assert.Equal(2, xero.TokenRequests.Count);
    }

    [Fact]
    public async Task Expired_sign_in_is_cleared()
    {
        var store = new MemoryTokenStore();
        store.WriteRefreshToken("old");
        var session = new XeroSession(new XeroOptions { ClientId = "abc" }, new HttpClient(new FakeXero { RejectRefresh = true }), store);

        await Assert.ThrowsAsync<XeroSignInException>(() => session.AccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Null(store.ReadRefreshToken());
    }

    [Fact]
    public async Task Wrong_state_is_refused()
    {
        var port = FreePort();
        using var receiver = new LoopbackReceiver(port);
        var wait = receiver.WaitForCodeAsync("expected", TestContext.Current.CancellationToken);
        await new HttpClient().GetAsync($"http://localhost:{port}/callback?code=X&state=forged", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<XeroSignInException>(() => wait);
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class FakeXero : HttpMessageHandler
    {
        public List<string> TokenRequests { get; } = [];
        public bool RejectRefresh { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (request.RequestUri!.Host == "identity.xero.com")
            {
                var body = await request.Content!.ReadAsStringAsync(cancel);
                TokenRequests.Add(body);
                if (RejectRefresh)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                var n = TokenRequests.Count;
                return Json($$"""{"access_token":"access-{{n}}","refresh_token":"refresh-{{n}}","expires_in":1800}""");
            }
            if (request.RequestUri.AbsolutePath == "/connections")
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                return Json("""[{"tenantId":"t1","tenantName":"Webignite (Pty) Ltd","tenantType":"ORGANISATION"},{"tenantId":"p1","tenantName":"Practice","tenantType":"PRACTICEMANAGER"}]""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
