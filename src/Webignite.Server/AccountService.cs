using System.Security.Cryptography;
using System.Text;
using Webignite.Core.Accounts;

namespace Webignite.Server;

/// <summary>
/// The server's own secret (from the environment, never from the repository): it seals two-factor secrets in the
/// database and makes believable password salts for emails that have no account, so the server doesn't give away
/// who has one.
/// </summary>
public sealed class ServerKeys(byte[] secret)
{
    private readonly byte[] sealKey = HKDF.Expand(HashAlgorithmName.SHA256, secret, 32, Encoding.UTF8.GetBytes("seal"));
    private readonly byte[] saltKey = HKDF.Expand(HashAlgorithmName.SHA256, secret, 32, Encoding.UTF8.GetBytes("salt"));

    public string Seal(string text)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(text);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(sealKey, 16))
            aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Open(string sealedText)
    {
        var all = Convert.FromBase64String(sealedText);
        var plain = new byte[all.Length - 28];
        using (var aes = new AesGcm(sealKey, 16))
            aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>The same made-up salt for an unknown email every time, so asking twice gives nothing away.</summary>
    public string FakeSalt(string email) =>
        Convert.ToBase64String(HMACSHA256.HashData(saltKey, Encoding.UTF8.GetBytes(email))[..AccountKeys.SaltBytes]);
}

/// <summary>A result for the endpoints: a value, or an HTTP status with a problem in plain words.</summary>
public sealed record Result<T>(T? Value, int Status = 200, Problem? Problem = null);

/// <summary>Making a failed <see cref="Result{T}"/>.</summary>
public static class Outcome
{
    public static Result<T> Fail<T>(int status, string message, string? need = null) => new(default, status, new Problem(message, need));
}

/// <summary>
/// Accounts on the sync server. The server never sees a password: the app sends a sign-in key made from it (see
/// <see cref="AccountKeys"/>), which is stored only as a slow hash. Too many wrong tries lock the account for a
/// while; two-factor codes work once; each device's refresh token works once, and an old one used again signs
/// that device out, as someone else may have it.
/// </summary>
public sealed class AccountService(ServerStore store, ServerKeys keys, TimeProvider clock)
{
    public static readonly TimeSpan AccessLife = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshLife = TimeSpan.FromDays(30);
    public static readonly TimeSpan InviteLife = TimeSpan.FromDays(7);
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);
    public const int TriesBeforeLock = 10;
    private const int KeyHashRounds = 100_000;

    private const string WrongDetails = "That email and password don't match an account.";

    private DateTimeOffset Now => clock.GetUtcNow();

    private static string NewId() => Guid.CreateVersion7().ToString("N");

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] KeyHash(string signInKey, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(signInKey), salt, KeyHashRounds, HashAlgorithmName.SHA256, 32);

    private static bool Usable(string? signInKey) => signInKey is { Length: >= 40 and <= 100 };

    private static string Device(string? name) => string.IsNullOrWhiteSpace(name) ? "A PC" : name.Trim()[..Math.Min(name.Trim().Length, 80)];

    /// <summary>How to stretch the password for an email; made up (but always the same) for an email with no account.</summary>
    public KdfInfo Kdf(string email)
    {
        var normal = AccountRules.Normal(email);
        return store.AccountByEmail(normal) is { } account
            ? new KdfInfo(account.Salt, account.Rounds)
            : new KdfInfo(keys.FakeSalt(normal), AccountKeys.Rounds);
    }

    public Result<SessionInfo> SignUp(SignUpRequest request)
    {
        if (AccountRules.EmailProblem(request.Email) is { } emailProblem)
            return Outcome.Fail<SessionInfo>(400, emailProblem);
        if (!Usable(request.SignInKey) || request.Rounds < AccountKeys.FewestRounds || request.Rounds > 10_000_000
            || !IsSalt(request.Salt))
            return Outcome.Fail<SessionInfo>(400, "Something was missing from the sign-up. Update Webignite and try again.");
        var email = AccountRules.Normal(request.Email);
        string workspaceId, role;
        string? newName = null, inviteHash = null;
        if (request.InviteCode is { Length: > 0 } code)
        {
            inviteHash = Hash(InviteCodes.Normal(code));
            if (store.WorkspaceOfInvite(inviteHash, Now) is not { } invited)
                return Outcome.Fail<SessionInfo>(400, "That invite code isn't right, or it has expired or been used. Ask for a new one.");
            (workspaceId, role) = (invited, AccountRoles.Member);
        }
        else
        {
            newName = string.IsNullOrWhiteSpace(request.WorkspaceName) ? "My workspace" : request.WorkspaceName.Trim()[..Math.Min(request.WorkspaceName.Trim().Length, 100)];
            (workspaceId, role) = (NewId(), AccountRoles.Owner);
        }
        var keySalt = RandomNumberGenerator.GetBytes(16);
        var account = new ServerStore.Account(NewId(), email, request.Salt, request.Rounds, KeyHash(request.SignInKey, keySalt), keySalt,
            null, null, -1, 0, null);
        if (!store.AddAccount(account, workspaceId, newName, role, inviteHash, Now))
            return Outcome.Fail<SessionInfo>(409, inviteHash is null
                ? "There's already an account for that email. Sign in instead."
                : "There's already an account for that email, or the invite was used meanwhile.");
        return new Result<SessionInfo>(StartSession(account.Id, request.DeviceName));
    }

    private static bool IsSalt(string text)
    {
        try
        {
            return Convert.FromBase64String(text).Length == AccountKeys.SaltBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public Result<SessionInfo> SignIn(SignInRequest request)
    {
        if (!Usable(request.SignInKey) || store.AccountByEmail(AccountRules.Normal(request.Email ?? "")) is not { } account)
            return Outcome.Fail<SessionInfo>(401, WrongDetails);
        if (account.LockedUntil is { } locked && locked > Now)
            return Outcome.Fail<SessionInfo>(429, "Too many wrong tries. Wait a quarter of an hour and try again.");
        if (!CryptographicOperations.FixedTimeEquals(KeyHash(request.SignInKey, account.KeySalt), account.KeyHash))
            return Failed(account);
        long? step = null;
        if (account.Totp is { } sealedSecret)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
                return Outcome.Fail<SessionInfo>(401, "Type the code from your authenticator app.", Needs.Code);
            step = Totp.Verify(keys.Open(sealedSecret), request.Code, Now, account.TotpLast);
            if (step is null && !store.UseRecoveryCode(account.Id, Hash(RecoveryCodeText.Normal(request.Code))))
                return Failed(account, "That code isn't right. Use the newest code from your authenticator app, or one of your recovery codes.", Needs.Code);
        }
        store.SignInWorked(account.Id, step);
        return new Result<SessionInfo>(StartSession(account.Id, request.DeviceName));
    }

    private Result<SessionInfo> Failed(ServerStore.Account account, string message = WrongDetails, string? need = null)
    {
        var failed = account.Failed + 1;
        store.SignInFailed(account.Id, failed >= TriesBeforeLock ? 0 : failed, failed >= TriesBeforeLock ? Now + LockFor : null);
        return Outcome.Fail<SessionInfo>(401, message, need);
    }

    private SessionInfo StartSession(string accountId, string? device)
    {
        var (access, refresh) = (NewToken(), NewToken());
        var session = new ServerStore.Session(NewId(), accountId, Device(device), Now + AccessLife, Now + RefreshLife);
        store.AddSession(session, Hash(access), Hash(refresh), Now);
        return new SessionInfo(access, refresh, session.AccessExpires, Info(accountId));
    }

    public Result<SessionInfo> Refresh(RefreshRequest request)
    {
        var hash = Hash(request.RefreshToken ?? "");
        if (store.SessionByRefresh(hash) is not { } session)
        {
            // An old refresh token used again: someone may have copied it, so that device is signed out.
            if (store.SessionByOldRefresh(hash) is { } taken)
                store.RemoveSession(taken.Id);
            return Outcome.Fail<SessionInfo>(401, "Sign in again.");
        }
        if (session.RefreshExpires <= Now)
        {
            store.RemoveSession(session.Id);
            return Outcome.Fail<SessionInfo>(401, "Sign in again.");
        }
        var (access, refresh) = (NewToken(), NewToken());
        if (!store.RotateSession(session.Id, hash, Hash(access), Now + AccessLife, Hash(refresh), Now + RefreshLife))
            return Outcome.Fail<SessionInfo>(401, "Sign in again.");
        return new Result<SessionInfo>(new SessionInfo(access, refresh, Now + AccessLife, Info(session.AccountId)));
    }

    /// <summary>The device behind an access token, while it's still good.</summary>
    public ServerStore.Session? Authenticate(string? accessToken) =>
        accessToken is { Length: > 0 } && store.SessionByAccess(Hash(accessToken)) is { } session && session.AccessExpires > Now ? session : null;

    public void SignOut(ServerStore.Session session) => store.RemoveSession(session.Id);

    public AccountInfo Info(string accountId)
    {
        var account = store.AccountById(accountId)!;
        var membership = store.MembershipOf(accountId)!;
        return new AccountInfo(account.Id, account.Email, account.Totp is not null,
            new WorkspaceInfo(membership.WorkspaceId, membership.Name, membership.Role,
                [.. store.Members(membership.WorkspaceId).Select(m => new MemberInfo(m.Email, m.Role))]));
    }

    // Two-factor

    public Result<TwoFactorSetup> StartTwoFactor(string accountId)
    {
        var account = store.AccountById(accountId)!;
        if (account.Totp is not null)
            return Outcome.Fail<TwoFactorSetup>(409, "Two-factor is on already.");
        var secret = Totp.NewSecret();
        store.SetTotpPending(accountId, keys.Seal(secret));
        return new Result<TwoFactorSetup>(new TwoFactorSetup(secret, Totp.Uri(secret, account.Email)));
    }

    public Result<RecoveryCodes> ConfirmTwoFactor(string accountId, string code)
    {
        var account = store.AccountById(accountId)!;
        if (account.TotpPending is not { } pending)
            return Outcome.Fail<RecoveryCodes>(409, "Start setting up two-factor first.");
        var secret = keys.Open(pending);
        if (Totp.Verify(secret, code, Now) is not { } step)
            return Outcome.Fail<RecoveryCodes>(400, "That code isn't right. Check the time on your phone is set automatically, and use the newest code.");
        var codes = Enumerable.Range(0, 8).Select(_ => RecoveryCodeText.New()).ToList();
        store.SetTotp(accountId, keys.Seal(secret), step, codes.Select(c => Hash(RecoveryCodeText.Normal(c))));
        return new Result<RecoveryCodes>(new RecoveryCodes(codes));
    }

    /// <summary>Two-factor off, with a current code to prove it's you; every other device is signed out.</summary>
    public Result<bool> StopTwoFactor(ServerStore.Session session, string code)
    {
        var account = store.AccountById(session.AccountId)!;
        if (account.Totp is not { } sealedSecret)
            return new Result<bool>(true);
        if (Totp.Verify(keys.Open(sealedSecret), code, Now, account.TotpLast) is null && !store.UseRecoveryCode(account.Id, Hash(RecoveryCodeText.Normal(code))))
            return Outcome.Fail<bool>(400, "That code isn't right.");
        store.SetTotp(account.Id, null, -1, []);
        return new Result<bool>(true);
    }

    // Invites

    public Result<InviteInfo> Invite(string accountId)
    {
        if (store.MembershipOf(accountId) is not { Role: AccountRoles.Owner } membership)
            return Outcome.Fail<InviteInfo>(403, "Only the workspace's owner can invite people.");
        var code = InviteCodes.New();
        var expires = Now + InviteLife;
        store.AddInvite(Hash(InviteCodes.Normal(code)), membership.WorkspaceId, accountId, expires);
        return new Result<InviteInfo>(new InviteInfo(code, expires));
    }
}

/// <summary>Invite codes: 12 letters and digits in groups of four, easy to read out (no 0/O or 1/I).</summary>
public static class InviteCodes
{
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New()
    {
        var chars = Enumerable.Range(0, 12).Select(_ => Letters[RandomNumberGenerator.GetInt32(Letters.Length)]).ToArray();
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    public static string Normal(string code) => new([.. code.ToUpperInvariant().Where(char.IsLetterOrDigit)]);
}

/// <summary>Two-factor recovery codes: 10 characters in two groups, each usable once.</summary>
public static class RecoveryCodeText
{
    private const string Letters = "abcdefghjkmnpqrstuvwxyz23456789";

    public static string New()
    {
        var chars = Enumerable.Range(0, 10).Select(_ => Letters[RandomNumberGenerator.GetInt32(Letters.Length)]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    public static string Normal(string? code) => new([.. (code ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
