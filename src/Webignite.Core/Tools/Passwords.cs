using System.Security.Cryptography;

namespace Webignite.Core.Tools;

public sealed record PasswordOptions(int Length = 20, bool Numbers = true, bool Symbols = true, bool AvoidLookalikes = true);

/// <summary>
/// Strong random passwords for client sites and services. Made from the system's secure random source,
/// and never stored by Webignite.
/// </summary>
public static class Passwords
{
    public const int MinLength = 8;
    public const int MaxLength = 64;

    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    // Symbols most sign-up forms and hosting panels accept, without quotes or backslashes that break scripts.
    private const string SymbolSet = "!@#$%^&*-_=+?";
    private const string Lookalikes = "Il1O0o";

    public static string Alphabet(PasswordOptions options)
    {
        var chars = Lower + Upper + (options.Numbers ? Digits : "") + (options.Symbols ? SymbolSet : "");
        return options.AvoidLookalikes ? new string(chars.Where(c => !Lookalikes.Contains(c)).ToArray()) : chars;
    }

    /// <summary>A password with at least one of each kind asked for, so picky sign-up forms accept it.</summary>
    public static string Make(PasswordOptions options)
    {
        var length = Math.Clamp(options.Length, MinLength, MaxLength);
        var alphabet = Alphabet(options);
        var groups = new List<string> { Lower, Upper };
        if (options.Numbers)
            groups.Add(Digits);
        if (options.Symbols)
            groups.Add(SymbolSet);
        groups = groups.Select(g => options.AvoidLookalikes ? new string(g.Where(c => !Lookalikes.Contains(c)).ToArray()) : g).ToList();

        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        // One of each kind at random, distinct places.
        var places = Enumerable.Range(0, length).ToArray();
        RandomNumberGenerator.Shuffle(places.AsSpan());
        for (var g = 0; g < groups.Count; g++)
            chars[places[g]] = groups[g][RandomNumberGenerator.GetInt32(groups[g].Length)];
        return new string(chars);
    }

    /// <summary>Roughly how hard it is to guess, in bits, for a password made here.</summary>
    public static int Bits(PasswordOptions options) =>
        (int)Math.Floor(Math.Clamp(options.Length, MinLength, MaxLength) * Math.Log2(Alphabet(options).Length));

    public static string Strength(int bits) => bits switch
    {
        < 60 => "Weak",
        < 80 => "Fair",
        < 110 => "Strong",
        _ => "Very strong",
    };
}
