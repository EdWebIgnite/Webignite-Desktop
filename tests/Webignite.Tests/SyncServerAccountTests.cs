using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Webignite.Core.Accounts;

namespace Webignite.Tests;

/// <summary>The sync server's accounts, through its real endpoints, with the server running in memory.</summary>
public sealed class SyncServerAccountTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-server-").FullName;
    private readonly WebApplicationFactory<Program> server;
    private readonly HttpClient http;

    public SyncServerAccountTests()
    {
        server = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Webignite:Database"] = Path.Combine(folder, "server.db"),
                ["Webignite:SignInPerMinute"] = "1000",
            }));
        });
        http = server.CreateClient();
    }

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static string Key(string seed) => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));

    private static readonly string Salt = Convert.ToBase64String(new byte[AccountKeys.SaltBytes]);

    private async Task<(HttpStatusCode Status, T? Body, Problem? Problem)> Post<T>(string path, object body, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        if (token is not null)
            request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, text.Length == 0 ? default : JsonSerializer.Deserialize<T>(text, Json), null)
            : (response.StatusCode, default, text.Length == 0 ? null : JsonSerializer.Deserialize<Problem>(text, Json));
    }

    private async Task<T?> Get<T>(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), Json);
    }

    private Task<(HttpStatusCode Status, SessionInfo? Body, Problem? Problem)> SignUp(string email, string key, string? workspace = "Webignite", string? invite = null) =>
        Post<SessionInfo>("/api/accounts", new SignUpRequest(email, Salt, AccountKeys.Rounds, key, workspace, invite, "Eddie's PC"));

    private Task<(HttpStatusCode Status, SessionInfo? Body, Problem? Problem)> SignIn(string email, string key, string? code = null) =>
        Post<SessionInfo>("/api/sessions", new SignInRequest(email, key, code, "Eddie's PC"));

    [Fact]
    public async Task Sign_up_sign_in_and_invite_a_partner_into_the_workspace()
    {
        var (status, session, _) = await SignUp("Eddie@Webignite.co.za", Key("eddie"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("eddie@webignite.co.za", "Webignite", AccountRoles.Owner), (session!.Account.Email, session.Account.Workspace.Name, session.Account.Workspace.Role));

        Assert.Equal(HttpStatusCode.Conflict, (await SignUp("eddie@webignite.co.za", Key("again"))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("eddie@webignite.co.za", Key("wrong"))).Status);
        var signedIn = (await SignIn("eddie@webignite.co.za", Key("eddie"))).Body!;

        var invite = (await Post<InviteInfo>("/api/workspace/invites", new { }, signedIn.AccessToken)).Body!;
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", invite.Code);
        var partner = await SignUp("jaco@webignite.co.za", Key("jaco"), workspace: null, invite: invite.Code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, partner.Status);
        Assert.Equal((session.Account.Workspace.Id, AccountRoles.Member), (partner.Body!.Account.Workspace.Id, partner.Body.Account.Workspace.Role));
        Assert.Equal(HttpStatusCode.BadRequest, (await SignUp("someone@else.co.za", Key("x"), invite: invite.Code)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post<InviteInfo>("/api/workspace/invites", new { }, partner.Body.AccessToken)).Status);

        var account = await Get<AccountInfo>("/api/account", signedIn.AccessToken);
        Assert.Equal(["eddie@webignite.co.za", "jaco@webignite.co.za"], account!.Workspace.Members.Select(m => m.Email));
    }

    [Fact]
    public async Task The_server_gives_the_same_salt_for_an_email_with_no_account()
    {
        var first = (await Post<KdfInfo>("/api/kdf", new { email = "nobody@nowhere.co.za" })).Body!;
        var again = (await Post<KdfInfo>("/api/kdf", new { email = "NOBODY@nowhere.co.za" })).Body!;
        Assert.Equal(first, again);
        Assert.Equal(AccountKeys.Rounds, first.Rounds);

        await SignUp("eddie@webignite.co.za", Key("eddie"));
        Assert.Equal(Salt, (await Post<KdfInfo>("/api/kdf", new { email = "eddie@webignite.co.za" })).Body!.Salt);
    }

    [Fact]
    public async Task Two_factor_codes_are_needed_once_on_and_work_once()
    {
        var session = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var setup = (await Post<TwoFactorSetup>("/api/two-factor", new { }, session.AccessToken)).Body!;
        var key = Base32.Decode(setup.Secret)!;
        string CodeNow() => Totp.Code(key, Totp.StepOf(DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.BadRequest, (await Post<RecoveryCodes>("/api/two-factor/confirm", new CodeRequest("000000"), session.AccessToken)).Status);
        var recovery = (await Post<RecoveryCodes>("/api/two-factor/confirm", new CodeRequest(CodeNow()), session.AccessToken)).Body!;
        Assert.Equal(8, recovery.Codes.Count);

        var needsCode = await SignIn("eddie@webignite.co.za", Key("eddie"));
        Assert.Equal((HttpStatusCode.Unauthorized, Needs.Code), (needsCode.Status, needsCode.Problem!.Need));
        // The code just used to turn it on can't be used again; the recovery code works once.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("eddie@webignite.co.za", Key("eddie"), CodeNow())).Status);
        Assert.Equal(HttpStatusCode.OK, (await SignIn("eddie@webignite.co.za", Key("eddie"), recovery.Codes[0].ToUpperInvariant())).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("eddie@webignite.co.za", Key("eddie"), recovery.Codes[0])).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("eddie@webignite.co.za", Key("wrong"), recovery.Codes[1])).Status);
    }

    [Fact]
    public async Task A_refresh_token_works_once_and_an_old_one_signs_the_device_out()
    {
        var session = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var next = (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(session.RefreshToken))).Body!;
        Assert.NotEqual(session.AccessToken, next.AccessToken);

        // The first refresh token again: whoever has it isn't trusted, and the device is signed out.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(session.RefreshToken))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(next.RefreshToken))).Status);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account");
        request.Headers.Authorization = new("Bearer", next.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Too_many_wrong_tries_lock_the_account_for_a_while()
    {
        await SignUp("eddie@webignite.co.za", Key("eddie"));
        for (var i = 0; i < 10; i++)
            await SignIn("eddie@webignite.co.za", Key("wrong" + i));
        var locked = await SignIn("eddie@webignite.co.za", Key("eddie"));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.Status);
    }

    [Fact]
    public async Task Signing_out_ends_the_device_and_nothing_works_without_a_token()
    {
        var session = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        using (var request = new HttpRequestMessage(HttpMethod.Delete, "/api/sessions/current"))
        {
            request.Headers.Authorization = new("Bearer", session.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, (await http.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<InviteInfo>("/api/workspace/invites", new { }, session.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<InviteInfo>("/api/workspace/invites", new { })).Status);
    }
}
