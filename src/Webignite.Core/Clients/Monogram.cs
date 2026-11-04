namespace Webignite.Core.Clients;

/// <summary>
/// The round badge beside a client or sender: up to two initials on a colour picked from the name, so the same
/// client always gets the same badge on every page and every day.
/// </summary>
public static class Monogram
{
    /// <summary>
    /// The colours a badge can have, from the palette's category set. Yellow is left out because white letters
    /// don't read on it, grey because it looks switched off, and red and green are never used for decoration.
    /// A badge with no name to go on is grey.
    /// </summary>
    public static IReadOnlyList<string> Colours { get; } = ["blue", "purple", "orange", "teal", "pink"];

    /// <summary>Words that say what kind of company it is rather than which one, so they don't make initials.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "pty", "ltd", "cc", "inc", "npc", "llc", "the", "and", "&", "co", "t/a",
    };

    /// <summary>
    /// "Karoo Coffee Roasters" is KC, "Fynbos Physio (Pty) Ltd" is FP, "Madiba" is MA, "jan@karoo.co.za" is JA.
    /// A name with no letters or digits gets "?".
    /// </summary>
    public static string Initials(string? name)
    {
        var text = (name ?? "").Trim();
        var at = text.IndexOf('@');
        if (at > 0)
            text = text[..at].Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        var words = text.Split([' ', '\t', '(', ')', ',', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .ToList();
        var meaningful = words.Where(w => !Filler.Contains(w)).ToList();
        if (meaningful.Count > 0)
            words = meaningful;

        return words.Count switch
        {
            0 => "?",
            1 => words[0].Length == 1 ? words[0].ToUpperInvariant() : words[0][..2].ToUpperInvariant(),
            _ => (words[0][..1] + words[1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>
    /// The badge colour for a name, one of <see cref="Colours"/>. It ignores letter case and extra spaces, the way
    /// client names are matched, and uses a fixed sum rather than .NET's string hash, which changes every run.
    /// </summary>
    public static string Colour(string? name)
    {
        var key = ClientDirectory.Key(name).ToUpperInvariant(); // upper case, so colours stay as they were
        if (key.Length == 0)
            return "grey";
        var hash = 2166136261u;
        foreach (var ch in key)
            hash = (hash ^ ch) * 16777619u;
        // Stir the bits so names that differ only near the end still land on different colours.
        hash ^= hash >> 15;
        hash *= 0x2c1b3c6du;
        hash ^= hash >> 12;
        hash *= 0x297a2d39u;
        hash ^= hash >> 15;
        return Colours[(int)(hash % (uint)Colours.Count)];
    }
}
