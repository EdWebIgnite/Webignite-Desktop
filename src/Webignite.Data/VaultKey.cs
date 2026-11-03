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

    /// <summary>Replaces the key, e.g. after restoring a backup. Only on Windows; tests pass their own.</summary>
    [SupportedOSPlatform("windows")]
    public static void Save(string dataDirectory, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("The vault key must be 32 bytes.", nameof(key));
        var path = Path.Combine(dataDirectory, "vault.key");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllBytes(path, Protect(key));
    }

    /// <summary>Locks a key to the signed-in Windows user, e.g. a restored key waiting for the next start.</summary>
    [SupportedOSPlatform("windows")]
    public static byte[] Protect(byte[] key) => ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    public static byte[] Unprotect(byte[] stored) => ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] LoadWithDpapi(string path) => LoadOrCreate(path,
        Path.Combine(Path.GetDirectoryName(path)!, "webignite.db"),
        Protect, Unprotect);

    /// <summary>
    /// Reads the key file, or makes a key the very first time. A database without its key file is never given a
    /// new key: every record in it would become unreadable and new ones would be written with a second key. That
    /// stops with <see cref="VaultKeyMissingException"/> instead. When the app and the connector start together
    /// for the first time, only one makes the key and the other reads it.
    /// </summary>
    internal static byte[] LoadOrCreate(string keyPath, string databasePath, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect)
    {
        if (File.Exists(keyPath))
            return unprotect(File.ReadAllBytes(keyPath));
        if (File.Exists(databasePath) && new FileInfo(databasePath).Length > 0)
            throw new VaultKeyMissingException(keyPath);

        var key = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        try
        {
            using var file = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(protect(key));
            return key;
        }
        catch (IOException) when (File.Exists(keyPath))
        {
            // The other program made it first: use that one.
            Thread.Sleep(100);
            return unprotect(File.ReadAllBytes(keyPath));
        }
    }
}

/// <summary>The database is there but the key that unlocks it is not.</summary>
public sealed class VaultKeyMissingException(string keyPath) : InvalidOperationException(
    $"Webignite's data is on this PC, but the key that unlocks it ({keyPath}) is missing. " +
    "Nothing was changed. Restore your latest backup, which carries the key, or put the key file back.")
{
    public string KeyPath { get; } = keyPath;
}
