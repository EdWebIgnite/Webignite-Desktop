using System.Net.Http.Json;
using System.Text.Json;
using Webignite.Core.Accounts;
using Webignite.Core.Sync;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>Where the sync server is and who's signed in on this PC (never the password or tokens: those are in Windows' vault).</summary>
public sealed record AccountSettings(string ServerAddress = "", string Email = "");

/// <summary>
/// Signing in to the Webignite sync server from this PC. The password never leaves the PC: it's stretched here
/// into a sign-in key (sent) and an encryption key (kept, for the workspace key in the next step). The refresh
/// token is kept in Windows Credential Manager, so the PC stays signed in; the access token only in memory.
/// </summary>
public sealed class AccountClient(LocalStore store, ISecretStore secrets, HttpMessageHandler? handler = null, string? licencePublicKey = null) : IDisposable
{
    private const string SettingName = "account";
    private const string RefreshName = "webignite-sync-refresh";
    private const string WorkspaceKeyName = "webignite-workspace-key";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient http = handler is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : new HttpClient(handler, disposeHandler: false);
    private string? accessToken;
    private string? lastFailure;
    private DateTimeOffset accessExpires;

    /// <summary>The encryption key made from the password at the last sign-in on this run; null until then.</summary>
    public byte[]? EncryptionKey { get; private set; }

    public AccountInfo? Account { get; private set; }

    /// <summary>
    /// The workspace key, which locks everything synced, once this PC has it (from signing in, signing up or joining).
    /// Kept in Windows Credential Manager so it survives a restart, never sent anywhere unlocked.
    /// </summary>
    public byte[]? WorkspaceKey => secrets.Read(WorkspaceKeyName) is { Length: > 0 } text ? Convert.FromBase64String(text) : null;

    public bool HasWorkspaceKey => secrets.Read(WorkspaceKeyName) is { Length: > 0 };

    /// <summary>A new account: who it is, and the recovery key to show once (to print or write down).</summary>
    public sealed record SignedUp(AccountInfo Account, string RecoveryKey);

    private void KeepWorkspaceKey(byte[] key) => secrets.Write(WorkspaceKeyName, Convert.ToBase64String(key));

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
            lastFailure = null;
            if (response.IsSuccessStatusCode)
                return new Reply<T>(text.Length == 0 ? default : JsonSerializer.Deserialize<T>(text, Json));
            var problem = text.Length > 0 && text.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<Problem>(text, Json) : null;
            return new Reply<T>(default, problem?.Message ?? $"The server said no ({(int)response.StatusCode}).", problem?.Need);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Sync tries every minute; while the internet is down the same failure is logged once, not every time.
            if (e.Message != lastFailure)
                AppLog.Write("Talking to the sync server", e);
            lastFailure = e.Message;
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
        secrets.Delete(WorkspaceKeyName);
    }

    /// <summary>
    /// A new account, with its own new workspace (and a new workspace key) or joining one with an invite code (whose
    /// secret half unlocks the workspace's key). Either way the key is locked with the new password and with a new
    /// recovery key, which comes back to be shown once.
    /// </summary>
    public async Task<Reply<SignedUp>> SignUpAsync(string email, string password, string? workspaceName, string? inviteCode, CancellationToken cancel = default)
    {
        if (AccountRules.EmailProblem(email) is { } emailProblem)
            return new Reply<SignedUp>(default, emailProblem);
        if (AccountRules.PasswordProblem(password, email) is { } passwordProblem)
            return new Reply<SignedUp>(default, passwordProblem);
        byte[] workspaceKey;
        string? serverPart = null, inviteCheck = null;
        if (!string.IsNullOrWhiteSpace(inviteCode))
        {
            if (WorkspaceKeys.ReadInviteCode(inviteCode) is not { } code)
                return new Reply<SignedUp>(default, "That invite code isn't complete. Copy the whole code you were given.");
            var opened = await Send<InviteOpened>(HttpMethod.Post, "api/invites/open", new OpenInviteRequest(code.ServerPart), signedIn: false, cancel);
            if (opened.Value is not { } invite)
                return new Reply<SignedUp>(default, opened.Problem);
            if (WorkspaceKeys.Unwrap(invite.WrappedKey, WorkspaceKeys.FromInviteSecret(code.Secret), WorkspaceKeys.ForInvite) is not { } key)
                return new Reply<SignedUp>(default, "That invite code isn't right. Copy the whole code you were given.");
            (workspaceKey, serverPart, inviteCheck) = (key, code.ServerPart, WorkspaceKeys.InviteCheck(code.Secret));
        }
        else
            workspaceKey = WorkspaceKeys.NewKey();
        var salt = AccountKeys.NewSalt();
        var keys = await Task.Run(() => AccountKeys.Derive(password, salt, AccountKeys.Rounds), cancel);
        var recoveryKey = WorkspaceKeys.NewRecoveryKey();
        var (recoveryLock, recoveryCheck) = WorkspaceKeys.FromRecoveryKey(WorkspaceKeys.ReadRecoveryKey(recoveryKey)!);
        var reply = await Send<SessionInfo>(HttpMethod.Post, "api/accounts",
            new SignUpRequest(AccountRules.Normal(email), Convert.ToBase64String(salt), AccountKeys.Rounds, keys.SignInKey,
                serverPart is null ? workspaceName : null, serverPart, DeviceName,
                WorkspaceKeys.Wrap(workspaceKey, keys.EncryptionKey, WorkspaceKeys.ForMember),
                WorkspaceKeys.Wrap(workspaceKey, recoveryLock, WorkspaceKeys.ForRecovery), recoveryCheck, inviteCheck),
            signedIn: false, cancel);
        if (reply.Value is not { } session)
            return new Reply<SignedUp>(default, reply.Problem, reply.Need);
        Keep(session);
        EncryptionKey = keys.EncryptionKey;
        KeepWorkspaceKey(workspaceKey);
        return new Reply<SignedUp>(new SignedUp(session.Account, recoveryKey));
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
        if (reply.Value is not { } session)
            return new Reply<AccountInfo>(default, reply.Problem, reply.Need);
        // The workspace key, unlocked with this password. If it doesn't open, the server handed over something else.
        if (WorkspaceKeys.Unwrap(session.WrappedKey, keys.EncryptionKey, WorkspaceKeys.ForMember) is not { } workspaceKey)
            return new Reply<AccountInfo>(default, "Signed in, but the workspace key wouldn't open with this password. Nothing was changed.");
        Keep(session);
        EncryptionKey = keys.EncryptionKey;
        KeepWorkspaceKey(workspaceKey);
        return new Reply<AccountInfo>(session.Account);
    }

    /// <summary>
    /// A forgotten password replaced with the recovery key: the workspace key is unlocked with it here, locked again
    /// with the new password, and every other PC is signed out. Nothing synced is lost. With two-factor on, the
    /// server answers that it needs a code first. A new recovery key replaces the old one and comes back to be shown once.
    /// </summary>
    public async Task<Reply<SignedUp>> RecoverAsync(string email, string recoveryKey, string newPassword, string? code = null, CancellationToken cancel = default)
    {
        if (AccountRules.EmailProblem(email) is { } emailProblem)
            return new Reply<SignedUp>(default, emailProblem);
        if (WorkspaceKeys.ReadRecoveryKey(recoveryKey) is not { } recovery)
            return new Reply<SignedUp>(default, "That isn't a whole recovery key. It's 52 letters and digits in groups of four.");
        if (AccountRules.PasswordProblem(newPassword, email) is { } passwordProblem)
            return new Reply<SignedUp>(default, passwordProblem);
        var (recoveryLock, check) = WorkspaceKeys.FromRecoveryKey(recovery);
        var locked = await Send<RecoveryLocked>(HttpMethod.Post, "api/recovery/key", new RecoveryRequest(AccountRules.Normal(email), check), signedIn: false, cancel);
        if (locked.Value is not { } wrapped)
            return new Reply<SignedUp>(default, locked.Problem);
        if (WorkspaceKeys.Unwrap(wrapped.WrappedKey, recoveryLock, WorkspaceKeys.ForRecovery) is not { } workspaceKey)
            return new Reply<SignedUp>(default, "That recovery key doesn't open this account's workspace key.");
        var salt = AccountKeys.NewSalt();
        var keys = await Task.Run(() => AccountKeys.Derive(newPassword, salt, AccountKeys.Rounds), cancel);
        var newRecoveryKey = WorkspaceKeys.NewRecoveryKey();
        var (newLock, newCheck) = WorkspaceKeys.FromRecoveryKey(WorkspaceKeys.ReadRecoveryKey(newRecoveryKey)!);
        var reply = await Send<SessionInfo>(HttpMethod.Post, "api/recovery/reset",
            new ResetRequest(AccountRules.Normal(email), check, Convert.ToBase64String(salt), AccountKeys.Rounds, keys.SignInKey,
                WorkspaceKeys.Wrap(workspaceKey, keys.EncryptionKey, WorkspaceKeys.ForMember), DeviceName,
                WorkspaceKeys.Wrap(workspaceKey, newLock, WorkspaceKeys.ForRecovery), newCheck, string.IsNullOrWhiteSpace(code) ? null : code.Trim()),
            signedIn: false, cancel);
        if (reply.Value is not { } session)
            return new Reply<SignedUp>(default, reply.Problem, reply.Need);
        Keep(session);
        EncryptionKey = keys.EncryptionKey;
        KeepWorkspaceKey(workspaceKey);
        return new Reply<SignedUp>(new SignedUp(session.Account, newRecoveryKey));
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

    /// <summary>
    /// An invite code for your partner: the server's half, then a secret half that unlocks the workspace key and is
    /// never sent to the server. Give the whole code to your partner.
    /// </summary>
    public async Task<Reply<InviteInfo>> InviteAsync(CancellationToken cancel = default)
    {
        if (WorkspaceKey is not { } workspaceKey)
            return new Reply<InviteInfo>(default, "Sign in again on this PC first, so it has the workspace key to share.");
        var secret = WorkspaceKeys.NewInviteSecret();
        var reply = await Send<InviteInfo>(HttpMethod.Post, "api/workspace/invites",
            new InviteRequest(WorkspaceKeys.Wrap(workspaceKey, WorkspaceKeys.FromInviteSecret(secret), WorkspaceKeys.ForInvite), WorkspaceKeys.InviteCheck(secret)),
            signedIn: true, cancel);
        return reply.Value is { } invite
            ? new Reply<InviteInfo>(invite with { Code = WorkspaceKeys.InviteCode(invite.Code, secret) })
            : reply;
    }

    private const string LicenceName = "licence";
    private readonly string licenceKey = licencePublicKey ?? LicenceKey.Public;

    /// <summary>Whether this copy of the app can check licences: it has the server's public key built in.</summary>
    public bool ChecksLicences => licenceKey.Length > 0;

    /// <summary>
    /// The licence kept on this PC, checked again each time it's read: signed by the server, for this workspace, and
    /// not from the future (a PC clock wound back doesn't stretch it). Null when there's none or it doesn't check out.
    /// </summary>
    public Licence? Licence => store.GetSetting<KeptLicence>(LicenceName) is { } kept && Licences.Read(kept.Token, licenceKey) is { } licence
        && Account is { } account && licence.WorkspaceId == account.Workspace.Id && !ClockWoundBack(licence)
            ? licence : null;

    private const string LatestSeenName = "licence-latest-seen";

    /// <summary>
    /// Whether this PC's clock is behind a time it has already seen: when the licence was issued, or the latest time
    /// it was read. Remembers the latest time each read, so winding the clock back doesn't bring back an older day.
    /// </summary>
    private bool ClockWoundBack(Licence licence)
    {
        var now = DateTimeOffset.UtcNow;
        var seen = store.GetSetting<DateTimeOffset?>(LatestSeenName) ?? DateTimeOffset.MinValue;
        if (now + TimeSpan.FromHours(1) < (licence.Issued > seen ? licence.Issued : seen))
            return true;
        if (now > seen)
            store.SetSetting<DateTimeOffset?>(LatestSeenName, now);
        return false;
    }

    /// <summary>
    /// Fetches the workspace's licence when the one kept is older than <paramref name="every"/> (or there's none),
    /// and keeps it only if it's signed by the server's key and for this workspace.
    /// </summary>
    public async Task<Reply<Licence>> RefreshLicenceAsync(TimeSpan every, DateTimeOffset now, CancellationToken cancel = default)
    {
        if (!ChecksLicences)
            return new Reply<Licence>(default, "Licences aren't set up in this copy of Webignite yet.");
        if (store.GetSetting<KeptLicence>(LicenceName) is { } kept && now - kept.Fetched < every && Licence is { } fresh)
            return new Reply<Licence>(fresh);
        var reply = await Send<LicenceReply>(HttpMethod.Get, "api/licence", null, signedIn: true, cancel);
        if (reply.Value is not { } signed)
            return new Reply<Licence>(Licence, reply.Problem);
        if (Licences.Read(signed.Token, licenceKey) is not { } licence || Account is not { } account || licence.WorkspaceId != account.Workspace.Id)
            return new Reply<Licence>(Licence, "The licence from the server didn't check out.");
        store.SetSetting(LicenceName, new KeptLicence(signed.Token, now));
        return new Reply<Licence>(licence);
    }

    /// <summary>Sends locked records to the workspace on the server.</summary>
    public Task<Reply<PushReply>> PushAsync(IReadOnlyList<SyncRecord> records, CancellationToken cancel = default) =>
        Send<PushReply>(HttpMethod.Post, "api/sync/push", new PushRequest(records), signedIn: true, cancel);

    /// <summary>Locked records changed on the server since the cursor.</summary>
    public Task<Reply<PullReply>> PullAsync(long since, CancellationToken cancel = default) =>
        Send<PullReply>(HttpMethod.Post, "api/sync/pull", new PullRequest(since), signedIn: true, cancel);

    public void Dispose()
    {
        http.Dispose();
        if (EncryptionKey is { } key)
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
    }
}
