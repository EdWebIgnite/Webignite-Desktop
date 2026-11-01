using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Webignite.Data;

/// <summary>The backup password is wrong, or the file was changed or damaged.</summary>
public sealed class BackupPasswordException(string message) : Exception(message);

/// <summary>
/// Encrypts a backup with a key made from the backup password (PBKDF2-SHA256), in 1 MB pieces of AES-256-GCM.
/// Each piece is bound to its position and the last one is marked, so pieces can't be reordered, dropped or cut off.
/// <code>
/// "WBAK" | version (1) | iterations (int32) | salt (16) | nonce prefix (4)
/// then per piece: length (int32) | ciphertext | tag (16); the piece flagged last ends the file
/// </code>
/// </summary>
public static class BackupCrypto
{
    private static readonly byte[] Magic = "WBAK"u8.ToArray();
    private const byte Version = 1;
    private const int SaltSize = 16;
    private const int PrefixSize = 4;
    private const int TagSize = 16;
    private const int HeaderSize = 4 + 1 + 4 + SaltSize + PrefixSize;
    public const int PieceSize = 1 << 20;
    public const int DefaultIterations = 600_000;
    private const int MaxIterations = 10_000_000;

    public static void Encrypt(Stream plain, Stream output, string password, int iterations = DefaultIterations)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[4] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(5), iterations);
        RandomNumberGenerator.Fill(header.AsSpan(9, SaltSize + PrefixSize));
        output.Write(header);

        using var aes = new AesGcm(Key(password, header.AsSpan(9, SaltSize), iterations), TagSize);
        var prefix = header.AsSpan(9 + SaltSize, PrefixSize).ToArray();
        var buffer = new byte[PieceSize];
        var next = new byte[PieceSize];
        var size = new byte[4];
        var length = ReadFull(plain, buffer);
        long index = 0;
        while (true)
        {
            // Read ahead one piece, so the last piece can be marked as last.
            var nextLength = length == PieceSize ? ReadFull(plain, next) : 0;
            var last = nextLength == 0;
            var cipher = new byte[length + TagSize];
            aes.Encrypt(Nonce(prefix, index), buffer.AsSpan(0, length), cipher.AsSpan(0, length), cipher.AsSpan(length),
                Associated(header, index, last));
            BinaryPrimitives.WriteInt32LittleEndian(size, length);
            output.Write(size);
            output.Write(cipher);
            if (last)
                return;
            (buffer, next) = (next, buffer);
            length = nextLength;
            index++;
        }
    }

    /// <summary>Throws <see cref="BackupPasswordException"/> when the password is wrong or the file was changed.</summary>
    public static void Decrypt(Stream input, Stream plain, string password)
    {
        var header = new byte[HeaderSize];
        if (ReadFull(input, header) != HeaderSize || !header.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("This isn't a Webignite backup file.");
        if (header[4] != Version)
            throw new InvalidDataException("This backup was made by a newer version of Webignite. Update the app first.");
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5));
        if (iterations is < 1 or > MaxIterations)
            throw new InvalidDataException("This backup file is damaged.");

        using var aes = new AesGcm(Key(password, header.AsSpan(9, SaltSize), iterations), TagSize);
        var prefix = header.AsSpan(9 + SaltSize, PrefixSize).ToArray();
        var size = new byte[4];
        long index = 0;
        while (true)
        {
            if (ReadFull(input, size) != 4)
                throw new BackupPasswordException("This backup file is cut short.");
            var length = BinaryPrimitives.ReadInt32LittleEndian(size);
            if (length is < 0 or > PieceSize)
                throw new InvalidDataException("This backup file is damaged.");
            var cipher = new byte[length + TagSize];
            if (ReadFull(input, cipher) != cipher.Length)
                throw new BackupPasswordException("This backup file is cut short.");
            var piece = new byte[length];
            bool last;
            if (TryDecrypt(aes, prefix, index, header, false, cipher, piece))
                last = false;
            else if (TryDecrypt(aes, prefix, index, header, true, cipher, piece))
                last = true;
            else
                throw new BackupPasswordException(index == 0
                    ? "The backup password is wrong, or the file was changed."
                    : "This backup file was changed or damaged.");
            plain.Write(piece);
            if (last)
            {
                if (input.ReadByte() != -1)
                    throw new BackupPasswordException("This backup file has extra data after the end.");
                return;
            }
            index++;
        }
    }

    private static bool TryDecrypt(AesGcm aes, byte[] prefix, long index, byte[] header, bool last, byte[] cipher, byte[] piece)
    {
        try
        {
            var length = piece.Length;
            aes.Decrypt(Nonce(prefix, index), cipher.AsSpan(0, length), cipher.AsSpan(length), piece, Associated(header, index, last));
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
    }

    private static byte[] Key(string password, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] Nonce(byte[] prefix, long index)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), index);
        return nonce;
    }

    private static byte[] Associated(byte[] header, long index, bool last)
    {
        var data = new byte[header.Length + 9];
        header.CopyTo(data, 0);
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(header.Length), index);
        data[^1] = last ? (byte)1 : (byte)0;
        return data;
    }

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}
