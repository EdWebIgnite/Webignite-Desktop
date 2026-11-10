using System.Security.Cryptography;
using System.Text;

namespace Webignite.Core.Accounts;

/// <summary>
/// What the app makes from a password, on the PC, before anything goes to the server. The password itself never
/// leaves the PC. From it come two keys that can't be worked out from each other:
/// <list type="bullet">
/// <item>the sign-in key, sent to the server to prove who you are (the server keeps only a hash of it);</item>
/// <item>the encryption key, which never leaves the PC and locks the workspace key, so the server can't read
/// anything that's synced.</item>
/// </list>
/// The password is stretched with PBKDF2-SHA256 over many rounds, so guessing passwords against a stolen copy is slow.
/// </summary>
public static class AccountKeys
{
    /// <summary>Rounds for new accounts (OWASP's figure for PBKDF2-SHA256).</summary>
    public const int Rounds = 600_000;

    /// <summary>The fewest rounds the app accepts from a server, so a tampered server can't weaken it.</summary>
    public const int FewestRounds = 600_000;

    public const int SaltBytes = 16;

    public sealed record Keys(string SignInKey, byte[] EncryptionKey);

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltBytes);

    /// <summary>The two keys for a password, salt and number of rounds.</summary>
    public static Keys Derive(string password, byte[] salt, int rounds)
    {
        if (rounds < FewestRounds)
            throw new ArgumentOutOfRangeException(nameof(rounds), "Too few rounds.");
        var master = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, rounds, HashAlgorithmName.SHA256, 32);
        try
        {
            var signIn = HKDF.Expand(HashAlgorithmName.SHA256, master, 32, Encoding.UTF8.GetBytes("webignite sign-in"));
            var encryption = HKDF.Expand(HashAlgorithmName.SHA256, master, 32, Encoding.UTF8.GetBytes("webignite encryption"));
            return new Keys(Convert.ToBase64String(signIn), encryption);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }
}

/// <summary>What makes an acceptable email and password.</summary>
public static class AccountRules
{
    public const int ShortestPassword = 12;

    public static string? EmailProblem(string? email)
    {
        var text = (email ?? "").Trim();
        if (text.Length is 0 or > 254)
            return "Type your email address.";
        var at = text.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 || at != text.LastIndexOf('@') || !text[(at + 1)..].Contains('.', StringComparison.Ordinal) || text.Any(char.IsWhiteSpace)
            ? "That doesn't look like an email address."
            : null;
    }

    /// <summary>Emails are kept and compared in lower case, trimmed.</summary>
    public static string Normal(string email) => email.Trim().ToLowerInvariant();

    public static string? PasswordProblem(string? password, string? email = null)
    {
        var text = password ?? "";
        if (text.Length < ShortestPassword)
            return $"Use at least {ShortestPassword} characters. A few words together is easy to remember and hard to guess.";
        if (text.Distinct().Count() < 5)
            return "Use more different characters.";
        if (email is { Length: > 0 } && text.Contains(email.Split('@')[0], StringComparison.OrdinalIgnoreCase) && email.Split('@')[0].Length >= 4)
            return "Don't use your email address in your password.";
        return null;
    }
}

/// <summary>The sync server's address: HTTPS only, except a server on this PC (for testing).</summary>
public static class SyncAddress
{
    /// <summary>
    /// Webignite's own sync server (on the Synology NAS, docs/synology-setup.md), filled in for anyone who hasn't
    /// picked one, so joining is only the invite code, an email and a password.
    /// </summary>
    public const string Ours = "https://sync.greatgraphicgroup.synology.me";

    /// <summary>The address kept on this PC, or ours when none has been kept yet.</summary>
    public static string OrOurs(string? kept) => string.IsNullOrWhiteSpace(kept) ? Ours : Normal(kept);

    public static string Normal(string address) => address.Trim().TrimEnd('/');

    public static string? Problem(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return "Type the sync server's address first, like https://sync.webignite.co.za.";
        if (!Uri.TryCreate(Normal(address), UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0)
            return "That isn't a web address. It looks like https://sync.webignite.co.za.";
        if (uri.Scheme == Uri.UriSchemeHttps)
            return null;
        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback ? null : "The sync server's address must start with https://, so nothing is sent unprotected.";
    }
}

/// <summary>When Webignite asks to sign in as it opens.</summary>
public static class StartRules
{
    /// <summary>Only when the welcome screen is switched on and nobody is signed in on this PC.</summary>
    public static bool AskToSignIn(bool switchedOn, bool signedIn) => switchedOn && !signedIn;
}
