namespace Webignite.Core.Accounts;

// What the app and the sync server send each other for accounts. JSON, snake_case on the wire.

/// <summary>How to stretch a password for an account: its salt (Base64) and rounds.</summary>
public sealed record KdfInfo(string Salt, int Rounds);

/// <summary>
/// A new account: its own new workspace (named), or joining one with an invite code. It brings the workspace key
/// locked twice: with the password's encryption key, and with the recovery key (plus a check that proves the
/// recovery key later). The server can open neither.
/// </summary>
/// <remarks>Joining also proves the invite's secret half with <paramref name="InviteCheck"/>, so the server's half alone isn't enough.</remarks>
public sealed record SignUpRequest(string Email, string Salt, int Rounds, string SignInKey, string? WorkspaceName, string? InviteCode, string DeviceName,
    string WrappedKey, string RecoveryWrappedKey, string RecoveryCheck, string? InviteCheck = null);

public sealed record SignInRequest(string Email, string SignInKey, string? Code, string DeviceName);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// A signed-in device: a short-lived access token for requests, and a refresh token for a new pair. Each refresh
/// token works once; using an old one again signs that device out.
/// </summary>
public sealed record SessionInfo(string AccessToken, string RefreshToken, DateTimeOffset AccessExpires, AccountInfo Account, string WrappedKey);

public sealed record AccountInfo(string Id, string Email, bool TwoFactor, WorkspaceInfo Workspace);

public sealed record WorkspaceInfo(string Id, string Name, string Role, IReadOnlyList<MemberInfo> Members);

public sealed record MemberInfo(string Email, string Role);

/// <summary>Turning two-factor on: the secret and the link an authenticator app reads.</summary>
public sealed record TwoFactorSetup(string Secret, string Uri);

public sealed record CodeRequest(string Code);

/// <summary>Two-factor turned on: one-time recovery codes, shown once, for when the phone is lost.</summary>
public sealed record RecoveryCodes(IReadOnlyList<string> Codes);

/// <summary>
/// An invite: the workspace key locked with the invite's secret half, which the server never sees, and a check made
/// from that secret half, which joining must match.
/// </summary>
public sealed record InviteRequest(string WrappedKey, string? SecretCheck = null);

/// <summary>The server's half of an invite code, and when it runs out.</summary>
public sealed record InviteInfo(string Code, DateTimeOffset Expires);

public sealed record OpenInviteRequest(string Code);

/// <summary>What an invite brings before signing up: the workspace's name and its key, still locked.</summary>
public sealed record InviteOpened(string WorkspaceName, string WrappedKey);

/// <summary>Asking for the recovery-locked workspace key, proving the recovery key with its check.</summary>
public sealed record RecoveryRequest(string Email, string Check);

public sealed record RecoveryLocked(string WrappedKey);

/// <summary>
/// A new password with the recovery key: new sign-in key and the workspace key locked with the new password. The
/// recovery key is replaced too (the old one stops working), and with two-factor on, a code is needed as well.
/// </summary>
public sealed record ResetRequest(string Email, string Check, string Salt, int Rounds, string SignInKey, string WrappedKey, string DeviceName,
    string? NewRecoveryWrappedKey = null, string? NewRecoveryCheck = null, string? Code = null);

/// <summary>The workspace's owner taking someone out of it: they can't sign in or sync any more.</summary>
public sealed record RemoveMemberRequest(string Email);

/// <summary>
/// A new password while signed in: the current password's sign-in key proves it's you, and the workspace key comes
/// locked with the new password. Every other device is signed out.
/// </summary>
public sealed record ChangePasswordRequest(string CurrentSignInKey, string Salt, int Rounds, string SignInKey, string WrappedKey);

/// <summary>A problem the server explains in plain words.</summary>
public sealed record Problem(string Message, string? Need = null);

/// <summary>What a sign-in still needs, in <see cref="Problem.Need"/>.</summary>
public static class Needs
{
    public const string Code = "code";
}

public static class AccountRoles
{
    public const string Owner = "owner";
    public const string Member = "member";
}
