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
    private static readonly string Locked = WorkspaceKeys.Wrap(WorkspaceKeys.NewKey(), WorkspaceKeys.NewKey(), WorkspaceKeys.ForMember);

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

    private static readonly string InviteSecretCheck = Key("the invite's secret half");

    private Task<(HttpStatusCode Status, SessionInfo? Body, Problem? Problem)> SignUp(string email, string key, string? workspace = "Webignite", string? invite = null,
        string? inviteCheck = null) =>
        Post<SessionInfo>("/api/accounts", new SignUpRequest(email, Salt, AccountKeys.Rounds, key, workspace, invite, "Eddie's PC", Locked, Locked, Key(email + "recovery"),
            invite is null ? null : inviteCheck ?? InviteSecretCheck));

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

        var invite = (await Post<InviteInfo>("/api/workspace/invites", new InviteRequest(Locked, InviteSecretCheck), signedIn.AccessToken)).Body!;
        Assert.Equal(("Webignite", Locked), ((await Post<InviteOpened>("/api/invites/open", new OpenInviteRequest(invite.Code))).Body!.WorkspaceName, Locked));
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", invite.Code);
        // The server's half alone isn't enough: without the check from the secret half, nobody gets in.
        Assert.Equal(HttpStatusCode.BadRequest, (await SignUp("guesser@else.co.za", Key("g"), workspace: null, invite: invite.Code, inviteCheck: Key("a guess"))).Status);
        var partner = await SignUp("jaco@webignite.co.za", Key("jaco"), workspace: null, invite: invite.Code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, partner.Status);
        Assert.Equal((session.Account.Workspace.Id, AccountRoles.Member), (partner.Body!.Account.Workspace.Id, partner.Body.Account.Workspace.Role));
        Assert.Equal(HttpStatusCode.BadRequest, (await SignUp("someone@else.co.za", Key("x"), invite: invite.Code)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post<InviteInfo>("/api/workspace/invites", new InviteRequest(Locked, InviteSecretCheck), partner.Body.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post<InviteOpened>("/api/invites/open", new OpenInviteRequest(invite.Code))).Status);

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
        // Worked out once and used twice. Asked for afresh each time, a run that crosses the half minute where
        // codes change would send a new code the second time, which the server rightly takes.
        var code = Totp.Code(key, Totp.StepOf(DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.BadRequest, (await Post<RecoveryCodes>("/api/two-factor/confirm", new CodeRequest("000000"), session.AccessToken)).Status);
        var recovery = (await Post<RecoveryCodes>("/api/two-factor/confirm", new CodeRequest(code), session.AccessToken)).Body!;
        Assert.Equal(8, recovery.Codes.Count);

        var needsCode = await SignIn("eddie@webignite.co.za", Key("eddie"));
        Assert.Equal((HttpStatusCode.Unauthorized, Needs.Code), (needsCode.Status, needsCode.Problem!.Need));
        // The code just used to turn it on can't be used again; the recovery code works once.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("eddie@webignite.co.za", Key("eddie"), code)).Status);
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<InviteInfo>("/api/workspace/invites", new InviteRequest(Locked, InviteSecretCheck), session.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<InviteInfo>("/api/workspace/invites", new InviteRequest(Locked, InviteSecretCheck))).Status);
    }

    [Fact]
    public async Task The_recovery_check_gets_the_locked_key_and_replaces_the_password_signing_everyone_out()
    {
        var first = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var check = Key("eddie@webignite.co.za" + "recovery");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<RecoveryLocked>("/api/recovery/key", new RecoveryRequest("eddie@webignite.co.za", Key("guess")))).Status);
        Assert.Equal(Locked, (await Post<RecoveryLocked>("/api/recovery/key", new RecoveryRequest("Eddie@webignite.co.za", check))).Body!.WrappedKey);

        // An app that doesn't bring a new recovery key is refused, so the key always changes.
        Assert.Equal(HttpStatusCode.BadRequest, (await Post<SessionInfo>("/api/recovery/reset",
            new ResetRequest("eddie@webignite.co.za", check, Salt, AccountKeys.Rounds, Key("new password"), Locked, "Eddie's laptop"))).Status);
        var newCheck = Key("the new recovery key");
        var reset = await Post<SessionInfo>("/api/recovery/reset",
            new ResetRequest("eddie@webignite.co.za", check, Salt, AccountKeys.Rounds, Key("new password"), Locked, "Eddie's laptop", Locked, newCheck));
        Assert.Equal(HttpStatusCode.OK, reset.Status);
        // The old recovery key stops working; the new one works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<RecoveryLocked>("/api/recovery/key", new RecoveryRequest("eddie@webignite.co.za", check))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Post<RecoveryLocked>("/api/recovery/key", new RecoveryRequest("eddie@webignite.co.za", newCheck))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("eddie@webignite.co.za", Key("eddie"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await SignIn("eddie@webignite.co.za", Key("new password"))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(first.RefreshToken))).Status);
    }

    [Fact]
    public async Task With_two_factor_on_the_recovery_key_alone_doesnt_reset_the_password()
    {
        var session = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var setup = (await Post<TwoFactorSetup>("/api/two-factor", new { }, session.AccessToken)).Body!;
        var key = Base32.Decode(setup.Secret)!;
        var codes = (await Post<RecoveryCodes>("/api/two-factor/confirm", new CodeRequest(Totp.Code(key, Totp.StepOf(DateTimeOffset.UtcNow))), session.AccessToken)).Body!;
        var check = Key("eddie@webignite.co.za" + "recovery");
        ResetRequest Reset(string? code) => new("eddie@webignite.co.za", check, Salt, AccountKeys.Rounds, Key("new password"), Locked, "Laptop", Locked, Key("new"), code);

        var needs = await Post<SessionInfo>("/api/recovery/reset", Reset(null));
        Assert.Equal((HttpStatusCode.Unauthorized, Needs.Code), (needs.Status, needs.Problem!.Need));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/recovery/reset", Reset("123456"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Post<SessionInfo>("/api/recovery/reset", Reset(codes.Codes[0]))).Status);
    }

    [Fact]
    public async Task A_refresh_token_from_any_time_before_signs_the_device_out()
    {
        var first = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var second = (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(first.RefreshToken))).Body!;
        var third = (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(second.RefreshToken))).Body!;
        // Two refreshes on, the very first token turns up again: the device is signed out all the same.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(first.RefreshToken))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(third.RefreshToken))).Status);
    }

    [Fact]
    public async Task Turning_two_factor_off_signs_out_the_other_devices()
    {
        var here = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var setup = (await Post<TwoFactorSetup>("/api/two-factor", new { }, here.AccessToken)).Body!;
        var key = Base32.Decode(setup.Secret)!;
        var codes = (await Post<RecoveryCodes>("/api/two-factor/confirm", new CodeRequest(Totp.Code(key, Totp.StepOf(DateTimeOffset.UtcNow))), here.AccessToken)).Body!;
        var other = (await SignIn("eddie@webignite.co.za", Key("eddie"), codes.Codes[0])).Body!;
        Assert.Equal(HttpStatusCode.NoContent, (await Post<object>("/api/two-factor/off", new CodeRequest(codes.Codes[1]), here.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(other.RefreshToken))).Status);
        Assert.NotNull(await Get<AccountInfo>("/api/account", here.AccessToken));
    }

    [Fact]
    public async Task A_pull_hands_back_at_most_its_byte_limit_and_says_there_is_more()
    {
        var session = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var big = new string('x', 500 * 1024);
        var kind = Webignite.Core.Sync.SyncKinds.All.First();
        for (var push = 0; push < 3; push++)
        {
            var records = Enumerable.Range(0, 6).Select(i => new Webignite.Core.Sync.SyncRecord(kind, $"r{push}-{i}", big, 0)).ToList();
            Assert.Equal(HttpStatusCode.OK, (await Post<Webignite.Core.Sync.PushReply>("/api/sync/push", new Webignite.Core.Sync.PushRequest(records), session.AccessToken)).Status);
        }
        var first = (await Post<Webignite.Core.Sync.PullReply>("/api/sync/pull", new Webignite.Core.Sync.PullRequest(0), session.AccessToken)).Body!;
        Assert.True(first.More);
        Assert.InRange(first.Records.Sum(r => (long)r.Payload.Length), 1, Webignite.Core.Sync.SyncLimits.PullBytes);
        var rest = (await Post<Webignite.Core.Sync.PullReply>("/api/sync/pull", new Webignite.Core.Sync.PullRequest(first.Cursor), session.AccessToken)).Body!;
        Assert.Equal(18, first.Records.Count + rest.Records.Count);
        Assert.False(rest.More);
    }

    [Fact]
    public async Task The_owner_takes_a_partner_out_and_they_cant_sign_in_or_sync()
    {
        var owner = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var invite = (await Post<InviteInfo>("/api/workspace/invites", new InviteRequest(Locked, InviteSecretCheck), owner.AccessToken)).Body!;
        var partner = (await SignUp("jaco@webignite.co.za", Key("jaco"), workspace: null, invite: invite.Code)).Body!;
        var spare = (await Post<InviteInfo>("/api/workspace/invites", new InviteRequest(Locked, InviteSecretCheck), owner.AccessToken)).Body!;

        Assert.Equal(HttpStatusCode.Forbidden, (await Post<AccountInfo>("/api/workspace/members/remove", new RemoveMemberRequest("eddie@webignite.co.za"), partner.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post<AccountInfo>("/api/workspace/members/remove", new RemoveMemberRequest("eddie@webignite.co.za"), owner.AccessToken)).Status);
        var after = await Post<AccountInfo>("/api/workspace/members/remove", new RemoveMemberRequest("Jaco@webignite.co.za"), owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, after.Status);
        Assert.Equal(["eddie@webignite.co.za"], after.Body!.Workspace.Members.Select(m => m.Email));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<Webignite.Core.Sync.PullReply>("/api/sync/pull", new Webignite.Core.Sync.PullRequest(0), partner.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("jaco@webignite.co.za", Key("jaco"))).Status);
        // An invite still open stops working too.
        Assert.Equal(HttpStatusCode.BadRequest, (await Post<InviteOpened>("/api/invites/open", new OpenInviteRequest(spare.Code))).Status);
    }

    [Fact]
    public async Task A_new_password_while_signed_in_needs_the_current_one_and_signs_out_the_other_pcs()
    {
        var here = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var other = (await SignIn("eddie@webignite.co.za", Key("eddie"))).Body!;
        ChangePasswordRequest Change(string current) => new(Key(current), Salt, AccountKeys.Rounds, Key("brand new"), Locked);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<object>("/api/account/password", Change("not it"), here.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Post<object>("/api/account/password", Change("eddie"), here.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(other.RefreshToken))).Status);
        Assert.NotNull(await Get<AccountInfo>("/api/account", here.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("eddie@webignite.co.za", Key("eddie"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await SignIn("eddie@webignite.co.za", Key("brand new"))).Status);
    }

    [Fact]
    public async Task Signing_out_the_other_pcs_keeps_this_one()
    {
        var here = (await SignUp("eddie@webignite.co.za", Key("eddie"))).Body!;
        var other = (await SignIn("eddie@webignite.co.za", Key("eddie"))).Body!;
        Assert.Equal(HttpStatusCode.NoContent, (await Post<object>("/api/sessions/others/sign-out", new { }, here.AccessToken)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post<SessionInfo>("/api/sessions/refresh", new RefreshRequest(other.RefreshToken))).Status);
        Assert.NotNull(await Get<AccountInfo>("/api/account", here.AccessToken));
    }

    [Fact]
    public void Wrong_tries_from_one_address_dont_lock_out_another()
    {
        var store = new Webignite.Server.ServerStore(Path.Combine(folder, "direct.db"));
        var accounts = new Webignite.Server.AccountService(store, new Webignite.Server.ServerKeys(RandomNumberGenerator.GetBytes(32)), TimeProvider.System);
        Assert.Null(accounts.SignUp(new SignUpRequest("eddie@webignite.co.za", Salt, AccountKeys.Rounds, Key("eddie"), "Webignite", null, "PC", Locked, Locked, Key("r"))).Problem);
        SignInRequest Try(string key) => new("eddie@webignite.co.za", Key(key), null, "PC");

        for (var i = 0; i < Webignite.Server.AccountService.TriesBeforeLock; i++)
            accounts.SignIn(Try("guess" + i), "203.0.113.9");
        Assert.Equal(429, accounts.SignIn(Try("eddie"), "203.0.113.9").Status);
        // Eddie at the office isn't locked out by someone guessing from elsewhere.
        Assert.Null(accounts.SignIn(Try("eddie"), "41.13.0.7").Problem);
    }

    [Fact]
    public async Task Sign_up_without_the_locked_keys_is_refused()
    {
        var (status, _, _) = await Post<SessionInfo>("/api/accounts",
            new SignUpRequest("eddie@webignite.co.za", Salt, AccountKeys.Rounds, Key("eddie"), "Webignite", null, "PC", "", Locked, Key("r")));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
