using System.Net.Http.Json;
using System.Text.Json;
using Webignite.Core.Accounts;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>Where the sync server is and who's signed in on this PC (never the password or tokens: those are in Windows' vault).</summary>
public sealed record AccountSettings(string ServerAddress = "", string Email = "");

/// <summary>
/// Signing in to the Webignite sync server from this PC. The password never leaves the PC: it's stretched here
/// into a sign-in key (sent) and an encryption key (kept, for the workspace key in the next step). The refresh
/// token is kept in Windows Credential Manager, so the PC stays signed in; the access token only in memory.
/// </summary>
public sealed class AccountClient(LocalStore store, ISecretStore secrets, HttpMessageHandler? handler = null) : IDisposable
{
    private const string SettingName = "account";
    private const string RefreshName = "webignite-sync-refresh";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient http = handler is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : new HttpClient(handler, disposeHandler: false);
    private string? accessToken;
    private DateTimeOffset accessExpires;

    /// <summary>The encryption key made from the password at the last sign-in on this run; null until then.</summary>
    public byte[]? EncryptionKey { get; private set; }

    public AccountInfo? Account { get; private set; }

    public sealed record Reply<T>(T? Value, string? Problem = null, string? Need = null)
    {
        public bool Worked => Problem is null;
    }

    public AccountSettings Settings
    {
        get => store.GetSetting<AccountSettings>(SettingName) ?? new AccountSettings();
        private set => store.SetSetting(SettingName, value);
    }

    /// <summary>Whether this PC has a sign-in kept (it may still need refreshing).</summary>
    public bool IsSignedIn => secrets.Read(RefreshName) is { Length: > 0 };

    /// <summary>Checks and keeps the server's address. Only HTTPS, except a server on this PC for testing.</summary>
    public string? UseServer(string address)
    {
        if (SyncAddress.Problem(address) is { } problem)
            return problem;
        Settings = Settings with { ServerAddress = SyncAddress.Normal(address) };
        return null;
    }

    private Uri Endpoint(string path) => new(new Uri(Settings.ServerAddress.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static string DeviceName => Environment.MachineName;

    private async Task<Reply<T>> Send<T>(HttpMethod method, string path, object? body, bool signedIn, CancellationToken cancel, bool retried = false)
    {
        if (SyncAddress.Problem(Settings.ServerAddress) is { } addressProblem)
            return new Reply<T>(default, addressProblem);
        if (signedIn && !await EnsureAccess(cancel))
            return new Reply<T>(default, "Sign in again.");
        try
        {
            using var request = new HttpRequestMessage(method, Endpoint(path));
            if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            if (signedIn)
                request.Headers.Authorization = new("Bearer", accessToken);
            using var response = await http.SendAsync(request, cancel);
            // The access token was replaced meanwhile (the kept sign-in was refreshed elsewhere): refresh once and try again.
            if (signedIn && !retried && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                accessToken = null;
                return await Send<T>(method, path, body, signedIn, cancel, retried: true);
            }
            var text = await response.Content.ReadAsStringAsync(cancel);
            if (response.IsSuccessStatusCode)
                return new Reply<T>(text.Length == 0 ? default : JsonSerializer.Deserialize<T>(text, Json));
            var problem = text.Length > 0 && text.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<Problem>(text, Json) : null;
            return new Reply<T>(default, problem?.Message ?? $"The server said no ({(int)response.StatusCode}).", problem?.Need);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Write("Talking to the sync server", e);
            return new Reply<T>(default, "Couldn't reach the sync server. Check the address and your internet connection.");
        }
    }

    // A fresh access token from the kept refresh token when the one in memory has (nearly) run out.
    private async Task<bool> EnsureAccess(CancellationToken cancel)
    {
        if (accessToken is not null && accessExpires > DateTimeOffset.UtcNow.AddMinutes(1))
            return true;
        if (secrets.Read(RefreshName) is not { Length: > 0 } refresh)
            return false;
        var reply = await Send<SessionInfo>(HttpMethod.Post, "api/sessions/refresh", new RefreshRequest(refresh), signedIn: false, cancel);
        if (reply.Value is not { } session)
        {
            if (reply.Problem == "Sign in again.")
                Forget();
            return false;
        }
        Keep(session);
        return true;
    }

    private void Keep(SessionInfo session)
    {
        accessToken = session.AccessToken;
        accessExpires = session.AccessExpires;
        Account = session.Account;
        secrets.Write(RefreshName, session.RefreshToken);
        Settings = Settings with { Email = session.Account.Email };
    }

    private void Forget()
    {
        accessToken = null;
        Account = null;
        EncryptionKey = null;
        secrets.Delete(RefreshName);
    }

    /// <summary>A new account, with its own workspace or joining one with an invite code.</summary>
    public async Task<Reply<AccountInfo>> SignUpAsync(string email, string password, string? workspaceName, string? inviteCode, CancellationToken cancel = default)
    {
        if (AccountRules.EmailProblem(email) is { } emailProblem)
            return new Reply<AccountInfo>(default, emailProblem);
        if (AccountRules.PasswordProblem(password, email) is { } passwordProblem)
            return new Reply<AccountInfo>(default, passwordProblem);
        var salt = AccountKeys.NewSalt();
        var keys = await Task.Run(() => AccountKeys.Derive(password, salt, AccountKeys.Rounds), cancel);
        var reply = await Send<SessionInfo>(HttpMethod.Post, "api/accounts",
            new SignUpRequest(AccountRules.Normal(email), Convert.ToBase64String(salt), AccountKeys.Rounds, keys.SignInKey,
                string.IsNullOrWhiteSpace(inviteCode) ? workspaceName : null, string.IsNullOrWhiteSpace(inviteCode) ? null : inviteCode, DeviceName),
            signedIn: false, cancel);
        return Finish(reply, keys);
    }

    /// <summary>Signs this PC in; with two-factor on, the first try answers that it needs a code.</summary>
    public async Task<Reply<AccountInfo>> SignInAsync(string email, string password, string? code = null, CancellationToken cancel = default)
    {
        if (AccountRules.EmailProblem(email) is { } emailProblem)
            return new Reply<AccountInfo>(default, emailProblem);
        var kdf = await Send<KdfInfo>(HttpMethod.Post, "api/kdf", new { email = AccountRules.Normal(email) }, signedIn: false, cancel);
        if (kdf.Value is not { } info)
            return new Reply<AccountInfo>(default, kdf.Problem);
        if (info.Rounds < AccountKeys.FewestRounds)
            return new Reply<AccountInfo>(default, "The sync server asked for weaker password protection than Webignite allows, so it wasn't trusted.");
        var keys = await Task.Run(() => AccountKeys.Derive(password, Convert.FromBase64String(info.Salt), info.Rounds), cancel);
        var reply = await Send<SessionInfo>(HttpMethod.Post, "api/sessions",
            new SignInRequest(AccountRules.Normal(email), keys.SignInKey, string.IsNullOrWhiteSpace(code) ? null : code, DeviceName), signedIn: false, cancel);
        return Finish(reply, keys);
    }

    private Reply<AccountInfo> Finish(Reply<SessionInfo> reply, AccountKeys.Keys keys)
    {
        if (reply.Value is not { } session)
            return new Reply<AccountInfo>(default, reply.Problem, reply.Need);
        Keep(session);
        EncryptionKey = keys.EncryptionKey;
        return new Reply<AccountInfo>(session.Account);
    }

    /// <summary>The account as the server has it now (workspace, members, two-factor).</summary>
    public async Task<Reply<AccountInfo>> RefreshAccountAsync(CancellationToken cancel = default)
    {
        var reply = await Send<AccountInfo>(HttpMethod.Get, "api/account", null, signedIn: true, cancel);
        if (reply.Value is { } account)
            Account = account;
        return reply;
    }

    public async Task SignOutAsync(CancellationToken cancel = default)
    {
        if (IsSignedIn)
            await Send<object>(HttpMethod.Delete, "api/sessions/current", null, signedIn: true, cancel);
        Forget();
    }

    public Task<Reply<TwoFactorSetup>> StartTwoFactorAsync(CancellationToken cancel = default) =>
        Send<TwoFactorSetup>(HttpMethod.Post, "api/two-factor", new { }, signedIn: true, cancel);

    public async Task<Reply<RecoveryCodes>> ConfirmTwoFactorAsync(string code, CancellationToken cancel = default)
    {
        var reply = await Send<RecoveryCodes>(HttpMethod.Post, "api/two-factor/confirm", new CodeRequest(code), signedIn: true, cancel);
        if (reply.Worked)
            await RefreshAccountAsync(cancel);
        return reply;
    }

    public async Task<Reply<object>> StopTwoFactorAsync(string code, CancellationToken cancel = default)
    {
        var reply = await Send<object>(HttpMethod.Post, "api/two-factor/off", new CodeRequest(code), signedIn: true, cancel);
        if (reply.Worked)
            await RefreshAccountAsync(cancel);
        return reply;
    }

    public Task<Reply<InviteInfo>> InviteAsync(CancellationToken cancel = default) =>
        Send<InviteInfo>(HttpMethod.Post, "api/workspace/invites", new { }, signedIn: true, cancel);

    public void Dispose()
    {
        http.Dispose();
        if (EncryptionKey is { } key)
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
    }
}
