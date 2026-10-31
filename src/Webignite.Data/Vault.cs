using System.Security.Cryptography;

namespace Webignite.Data;

/// <summary>
/// Encrypts everything written to the local database with AES-256-GCM. Each value gets a fresh
/// 12-byte nonce, stored in front of the ciphertext and followed by the 16-byte tag.
/// </summary>
public sealed class Vault
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] key;

    public Vault(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("The vault key must be 32 bytes.", nameof(key));
        this.key = (byte[])key.Clone();
    }

    public byte[] Encrypt(byte[] plain, string context)
    {
        var output = new byte[NonceSize + plain.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length), Context(context));
        return output;
    }

    /// <summary>Throws <see cref="CryptographicException"/> if the data was changed or belongs to another record.</summary>
    public byte[] Decrypt(byte[] sealedData, string context)
    {
        if (sealedData.Length < NonceSize + TagSize)
            throw new CryptographicException("Stored data is too short to be valid.");
        var length = sealedData.Length - NonceSize - TagSize;
        var plain = new byte[length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(sealedData.AsSpan(0, NonceSize), sealedData.AsSpan(NonceSize, length),
            sealedData.AsSpan(NonceSize + length), plain, Context(context));
        return plain;
    }

    // Binding the record id into the tag stops one row's data being swapped into another.
    private static byte[] Context(string context) => System.Text.Encoding.UTF8.GetBytes(context);
}
