using System.Security.Cryptography;

namespace Webignite.Core.Accounts;

/// <summary>
/// Two-factor codes from an authenticator app (Google Authenticator, Microsoft Authenticator, 1Password and so on):
/// the time-based one-time passwords of RFC 6238, six digits every 30 seconds, with the shared secret in Base32.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    /// <summary>A new secret: 20 random bytes, as the Base32 text the app shows and the authenticator takes.</summary>
    public static string NewSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    /// <summary>The 30-second step a moment falls in.</summary>
    public static long StepOf(DateTimeOffset at) => at.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    /// <summary>The code for a step.</summary>
    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xFF);
            step >>= 8;
        }
#pragma warning disable CA5350 // HMAC-SHA1 is what RFC 6238 and every authenticator app use for these codes.
        var hash = HMACSHA1.HashData(secret, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The step a typed code belongs to, allowing one step either side for a clock that's a little out, or null when
    /// it doesn't fit. A step at or before <paramref name="lastUsed"/> is refused, so a code works only once.
    /// </summary>
    public static long? Verify(string secret, string? typed, DateTimeOffset now, long lastUsed = -1)
    {
        var code = new string((typed ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != Digits || Base32.Decode(secret) is not { } key)
            return null;
        var current = StepOf(now);
        for (var step = current - 1; step <= current + 1; step++)
            if (step > lastUsed && CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(Code(key, step)), System.Text.Encoding.ASCII.GetBytes(code)))
                return step;
        return null;
    }

    /// <summary>The link an authenticator app reads from a QR code.</summary>
    public static string Uri(string secret, string account, string issuer = "Webignite") =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={secret}&issuer={System.Uri.EscapeDataString(issuer)}&digits={Digits}&period={(int)Step.TotalSeconds}";
}

/// <summary>Base32 (RFC 4648, no padding), as authenticator secrets are written.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var text = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            text.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return text.ToString();
    }

    /// <summary>The bytes, ignoring spaces, dashes, case and padding; null when it isn't Base32.</summary>
    public static byte[]? Decode(string? text)
    {
        var clean = (text ?? "").ToUpperInvariant().Where(c => c is not (' ' or '-' or '=')).ToArray();
        if (clean.Length == 0)
            return null;
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}
