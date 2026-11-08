using System.Security.Cryptography;
using System.Text;

namespace Webignite.Core.Accounts;

/// <summary>
/// The workspace key: one random key per workspace that locks everything synced. The server only ever holds it
/// locked (wrapped), never as it is:
/// <list type="bullet">
/// <item>for each person, locked with the encryption key made from their password;</item>
/// <item>for each person, locked with their recovery key, for when the password is forgotten;</item>
/// <item>for an invite, locked with a secret that's part of the invite code and never sent to the server.</item>
/// </list>
/// Locking is AES-256-GCM, so a wrong key or a changed lock is noticed rather than giving rubbish.
/// </summary>
public static class WorkspaceKeys
{
    public const int KeyBytes = 32;

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeyBytes);

    /// <summary>Locks a key (or any small secret) with another key: Base64 of nonce, tag and locked bytes.</summary>
    public static string Wrap(byte[] secret, byte[] with, string purpose)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[secret.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(with, 16))
            aes.Encrypt(nonce, secret, cipher, tag, Encoding.UTF8.GetBytes(purpose));
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>The key again, or null when the key used doesn't fit or the lock was changed.</summary>
    public static byte[]? Unwrap(string? wrapped, byte[] with, string purpose)
    {
        try
        {
            var all = Convert.FromBase64String(wrapped ?? "");
            if (all.Length < 29)
                return null;
            var plain = new byte[all.Length - 28];
            using var aes = new AesGcm(with, 16);
            aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(purpose));
            return plain;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    // What each lock is for, bound into it, so one can't be passed off as another.
    public const string ForMember = "webignite workspace key for a member";
    public const string ForRecovery = "webignite workspace key for recovery";
    public const string ForInvite = "webignite workspace key for an invite";

    // Recovery keys

    /// <summary>
    /// A recovery key: 32 random bytes written as 52 letters and digits in groups of four, to print or write down.
    /// With it, a forgotten password can be replaced without losing anything synced.
    /// </summary>
    public static string NewRecoveryKey()
    {
        var text = Base32.Encode(RandomNumberGenerator.GetBytes(KeyBytes));
        return string.Join('-', Enumerable.Range(0, (text.Length + 3) / 4).Select(i => text.Substring(i * 4, Math.Min(4, text.Length - i * 4))));
    }

    /// <summary>The bytes of a typed recovery key (spacing, dashes and case don't matter), or null when it isn't one.</summary>
    public static byte[]? ReadRecoveryKey(string? typed) =>
        Base32.Decode(typed) is { Length: KeyBytes } bytes ? bytes : null;

    /// <summary>From a recovery key: the key that locks the workspace key, and a check the server keeps (hashed) to know it's yours.</summary>
    public static (byte[] Lock, string Check) FromRecoveryKey(byte[] recovery) =>
        (HKDF.Expand(HashAlgorithmName.SHA256, recovery, KeyBytes, Encoding.UTF8.GetBytes("webignite recovery lock")),
            Convert.ToBase64String(HKDF.Expand(HashAlgorithmName.SHA256, recovery, KeyBytes, Encoding.UTF8.GetBytes("webignite recovery check"))));

    // Invites

    /// <summary>
    /// The secret half of an invite code: kept by the person inviting and their partner, never by the server. It
    /// locks the workspace key for the invite.
    /// </summary>
    public static string NewInviteSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static byte[] FromInviteSecret(string secret) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Base32.Decode(secret) ?? [], KeyBytes, [], Encoding.UTF8.GetBytes("webignite invite lock"));

    /// <summary>The code to give your partner: the server's part, then the secret part.</summary>
    public static string InviteCode(string serverPart, string secret) => $"{serverPart}-{Group(secret)}";

    /// <summary>A typed invite code split into the server's part (12 characters) and the secret part; null when it isn't one.</summary>
    public static (string ServerPart, string Secret)? ReadInviteCode(string? typed)
    {
        var clean = new string([.. (typed ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit)]);
        if (clean.Length != 12 + 32)
            return null;
        var secret = clean[12..];
        return Base32.Decode(secret) is { Length: 20 } ? (clean[..12], secret) : null;
    }

    private static string Group(string text) =>
        string.Join('-', Enumerable.Range(0, (text.Length + 3) / 4).Select(i => text.Substring(i * 4, Math.Min(4, text.Length - i * 4))));
}
