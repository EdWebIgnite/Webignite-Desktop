using System.Text.RegularExpressions;

namespace Webignite.Core.Writing;

/// <summary>
/// A dictionary to check words against: Windows' own on a PC, so words added there are known here too.
/// </summary>
public interface ISpelling
{
    /// <summary>The language checked, e.g. en-ZA.</summary>
    string Language { get; }

    bool IsWrong(string word);

    IReadOnlyList<string> Suggest(string word);

    /// <summary>Adds the word to the dictionary for good.</summary>
    void Add(string word);

    /// <summary>Stops marking the word until the app closes.</summary>
    void Ignore(string word);

    /// <summary>Goes up when a word is added or ignored, so marks drawn before are worked out again.</summary>
    int Revision { get; }
}

/// <summary>A word marked as misspelt: where it starts in the paragraph, and how long it is.</summary>
public readonly record struct Misspelt(int Start, int Length, string Word);

/// <summary>Which words in writing are checked, and which are wrong.</summary>
public static partial class Spelling
{
    /// <summary>Languages tried in turn when the one picked isn't on the PC.</summary>
    public static readonly IReadOnlyList<string> Fallbacks = ["en-ZA", "en-GB", "en-US"];

    /// <summary>
    /// The words worth checking. Left out: web and email addresses, file names, anything with a digit in it (R1 500,
    /// 10am, INV-0107), capitals only (VAT, PDF), names with a capital inside (WordPress, iPhone), single letters,
    /// and {name} and {client}.
    /// </summary>
    public static IEnumerable<Misspelt> Words(string text)
    {
        foreach (Match chunk in Chunk().Matches(text))
        {
            var part = chunk.Value;
            if (part.Contains('@') || part.Contains("://", StringComparison.Ordinal) || part.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                || part.Any(char.IsDigit) || part.Contains('{') || part.Contains('\\') || part.Contains('/') || IsDotted(part))
                continue;
            foreach (Match word in Word().Matches(part))
            {
                var value = word.Value;
                if (value.Length < 2 || value.All(c => !char.IsLetter(c) || char.IsUpper(c)) || value.Skip(1).Any(char.IsUpper))
                    continue;
                yield return new Misspelt(chunk.Index + word.Index, value.Length, value);
            }
        }
    }

    /// <summary>
    /// The misspelt words in a paragraph. The word the caret is in is left alone while it's still being typed.
    /// </summary>
    public static IReadOnlyList<Misspelt> Mistakes(string text, ISpelling checker, int? typingAt = null) =>
        [.. Words(text).Where(w => !(typingAt is { } at && at >= w.Start && at <= w.Start + w.Length)).Where(w => checker.IsWrong(w.Word))];

    /// <summary>The misspelt word at a place in a paragraph, if there is one.</summary>
    public static Misspelt? At(string text, int offset, ISpelling checker) =>
        Words(text).Where(w => offset >= w.Start && offset <= w.Start + w.Length).Select(w => (Misspelt?)w).FirstOrDefault(w => checker.IsWrong(w!.Value.Word));

    /// <summary>
    /// The language to use: the one picked when the PC has it, else South African English, British, then American,
    /// else whatever the PC has.
    /// </summary>
    public static string? Pick(string? wanted, IReadOnlyCollection<string> available)
    {
        string? Find(string tag) => available.FirstOrDefault(a => string.Equals(a, tag, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(wanted) && Find(wanted.Trim()) is { } exact)
            return exact;
        foreach (var tag in Fallbacks)
        {
            if (Find(tag) is { } found)
                return found;
        }
        return available.FirstOrDefault(a => a.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? available.FirstOrDefault();
    }

    // A name with a dot inside, such as webignite.co.za or invoice.pdf; a full stop at the end is fine.
    private static bool IsDotted(string part) => part.TrimEnd('.', ',', '!', '?', ';', ':', ')', '"', '\'', '’').Contains('.');

    [GeneratedRegex(@"\S+")]
    private static partial Regex Chunk();

    [GeneratedRegex(@"\p{L}+(?:['’]\p{L}+)*")]
    private static partial Regex Word();
}

/// <summary>Spell check while writing: on or off, and the language when not the default.</summary>
public sealed record SpellCheckSettings
{
    public bool On { get; init; } = true;

    /// <summary>A language tag such as en-ZA; empty means South African English, or the closest the PC has.</summary>
    public string? Language { get; init; }
}
