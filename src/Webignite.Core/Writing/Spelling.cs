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
    /// <summary>Languages tried in turn when none of those picked is on the PC: British English first, as preferred.</summary>
    public static readonly IReadOnlyList<string> Fallbacks = ["en-GB", "en-ZA", "en-US"];

    /// <summary>The languages checked unless others are picked: British English (preferred), South African English and Afrikaans.</summary>
    public static readonly IReadOnlyList<string> Usual = ["en-GB", "en-ZA", "af-ZA"];

    /// <summary>The languages wanted: those picked, an older single pick, or the usual three.</summary>
    public static IReadOnlyList<string> Wanted(SpellCheckSettings settings) =>
        settings.Languages is { Count: > 0 } picked ? picked
        : !string.IsNullOrWhiteSpace(settings.Language) ? [settings.Language.Trim()]
        : Usual;

    /// <summary>
    /// The wanted languages the PC has, in the order wanted (the first gives suggestions first). When it has none
    /// of them, the closest it has, so spelling is still checked.
    /// </summary>
    public static IReadOnlyList<string> PickAll(IReadOnlyList<string> wanted, IReadOnlyCollection<string> available)
    {
        var found = wanted.Select(w => available.FirstOrDefault(a => string.Equals(a, w.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return found.Count > 0 ? found : Pick(null, available) is { } closest ? [closest] : [];
    }

    /// <summary>The wanted languages the PC doesn't have, to say how to add them.</summary>
    public static IReadOnlyList<string> Missing(IReadOnlyList<string> wanted, IReadOnlyCollection<string> available) =>
        [.. wanted.Where(w => !available.Any(a => string.Equals(a, w.Trim(), StringComparison.OrdinalIgnoreCase)))];

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
    /// The language to use: the one picked when the PC has it, else British English, South African, then American,
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

/// <summary>Spell check while writing: on or off, and the languages when not the usual three.</summary>
public sealed record SpellCheckSettings
{
    public bool On { get; init; } = true;

    /// <summary>A single language tag, as picked before several could be; read only when <see cref="Languages"/> is empty.</summary>
    public string? Language { get; init; }

    /// <summary>The languages to check in, the preferred first; empty means <see cref="Spelling.Usual"/>.</summary>
    public IReadOnlyList<string>? Languages { get; init; }
}

/// <summary>
/// Several dictionaries checked together, for writing in more than one language (English and Afrikaans in one
/// mail): a word is wrong only when none of them knows it. Suggestions come from the first, the preferred
/// language, then the others; a word added goes in the first.
/// </summary>
public sealed class MultiSpelling(IReadOnlyList<ISpelling> checkers) : ISpelling, IDisposable
{
    public IReadOnlyList<ISpelling> Checkers { get; } = checkers;

    public string Language => string.Join(", ", Checkers.Select(c => c.Language));

    public int Revision => Checkers.Sum(c => c.Revision);

    public bool IsWrong(string word) => Checkers.Count > 0 && Checkers.All(c => c.IsWrong(word));

    public IReadOnlyList<string> Suggest(string word) =>
        [.. Checkers.SelectMany(c => c.Suggest(word)).Distinct(StringComparer.Ordinal).Take(6)];

    public void Add(string word)
    {
        if (Checkers.Count > 0)
            Checkers[0].Add(word);
    }

    public void Ignore(string word)
    {
        foreach (var checker in Checkers)
            checker.Ignore(word);
    }

    public void Dispose()
    {
        foreach (var checker in Checkers)
            (checker as IDisposable)?.Dispose();
    }
}
