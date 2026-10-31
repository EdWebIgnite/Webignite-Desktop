using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Webignite.Data;

/// <summary>
/// Where the database key comes from. On Windows it is a random key kept in a file that only the
/// signed-in Windows user can unlock (DPAPI). WEBIGNITE_VAULT_KEY (base64, 32 bytes) overrides it,
/// for tests and for moving to another machine.
/// </summary>
public static class VaultKey
{
    public const string EnvironmentVariable = "WEBIGNITE_VAULT_KEY";
    private static readonly byte[] Entropy = "Webignite Desktop vault v1"u8.ToArray();

    public static byte[] Load(string dataDirectory)
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } fromEnvironment)
        {
            var key = Convert.FromBase64String(fromEnvironment);
            return key.Length == 32 ? key : throw new InvalidOperationException($"{EnvironmentVariable} must be 32 bytes, base64 encoded.");
        }
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Set {EnvironmentVariable} to run outside Windows.");
        return LoadWithDpapi(Path.Combine(dataDirectory, "vault.key"));
    }

    [SupportedOSPlatform("windows")]
    private static byte[] LoadWithDpapi(string path)
    {
        if (File.Exists(path))
            return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);

        var key = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
        return key;
    }
}
