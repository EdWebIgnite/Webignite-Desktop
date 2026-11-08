namespace Webignite.Core.Accounts;

// What the app and the sync server send each other for accounts. JSON, snake_case on the wire.

/// <summary>How to stretch a password for an account: its salt (Base64) and rounds.</summary>
public sealed record KdfInfo(string Salt, int Rounds);

/// <summary>A new account: its own new workspace (named), or joining one with an invite code.</summary>
public sealed record SignUpRequest(string Email, string Salt, int Rounds, string SignInKey, string? WorkspaceName, string? InviteCode, string DeviceName);

public sealed record SignInRequest(string Email, string SignInKey, string? Code, string DeviceName);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// A signed-in device: a short-lived access token for requests, and a refresh token for a new pair. Each refresh
/// token works once; using an old one again signs that device out.
/// </summary>
public sealed record SessionInfo(string AccessToken, string RefreshToken, DateTimeOffset AccessExpires, AccountInfo Account);

public sealed record AccountInfo(string Id, string Email, bool TwoFactor, WorkspaceInfo Workspace);

public sealed record WorkspaceInfo(string Id, string Name, string Role, IReadOnlyList<MemberInfo> Members);

public sealed record MemberInfo(string Email, string Role);

/// <summary>Turning two-factor on: the secret and the link an authenticator app reads.</summary>
public sealed record TwoFactorSetup(string Secret, string Uri);

public sealed record CodeRequest(string Code);

/// <summary>Two-factor turned on: one-time recovery codes, shown once, for when the phone is lost.</summary>
public sealed record RecoveryCodes(IReadOnlyList<string> Codes);

public sealed record InviteInfo(string Code, DateTimeOffset Expires);

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
