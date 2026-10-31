using System.Security.Cryptography;
using System.Text;

namespace Webignite.Xero.Auth;

/// <summary>Proof Key for Code Exchange (RFC 7636): lets a desktop app sign in without a client secret.</summary>
public static class Pkce
{
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
