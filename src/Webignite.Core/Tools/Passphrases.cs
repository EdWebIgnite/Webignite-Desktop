using System.Security.Cryptography;

namespace Webignite.Core.Tools;

/// <summary>A word password's makeup: how many words, and whether a number and a symbol go on the end.</summary>
public sealed record PassphraseOptions(int Words = 3, bool Number = true, bool Symbol = true);

/// <summary>
/// Passwords made of words, such as RedVelvetLantern47$: easier to read out, type and remember than random
/// characters. A describing word first, then things, each with a capital, then two digits and a symbol, so forms
/// that ask for capitals, numbers and symbols accept it. Picked with the system's secure random source.
/// </summary>
public static class Passphrases
{
    public const int MinWords = 2;
    public const int MaxWords = 6;

    // Symbols most sign-up forms accept, without quotes or backslashes that break scripts.
    private const string SymbolSet = "!@#$%&*?";

    /// <summary>Describing words: plain, everyday and inoffensive.</summary>
    public static readonly IReadOnlyList<string> Describing =
    [
        "amber", "azure", "bold", "brave", "bright", "brisk", "calm", "candid", "cheery", "chilly", "clever", "cosy",
        "crisp", "curly", "dainty", "dapper", "daring", "dusty", "eager", "early", "easy", "elder", "fancy", "fast",
        "fiery", "firm", "fluffy", "fond", "frosty", "funny", "gentle", "giant", "glad", "golden", "grand", "happy",
        "hardy", "hasty", "hazy", "heavy", "honest", "humble", "icy", "jolly", "jumpy", "keen", "kind", "large",
        "lazy", "lemon", "light", "lively", "lofty", "loud", "lucky", "lunar", "magic", "merry", "mighty", "mild",
        "minty", "misty", "modern", "moody", "neat", "nimble", "noble", "odd", "olive", "orange", "plain", "plucky",
        "plump", "polite", "proud", "purple", "quick", "quiet", "rapid", "rare", "ready", "red", "rich", "robust",
        "rosy", "round", "royal", "rustic", "rusty", "sandy", "shiny", "silent", "silver", "simple", "sleek", "slim",
        "smart", "smooth", "snowy", "soft", "solar", "solid", "spicy", "steady", "stormy", "strong", "sturdy",
        "sunny", "super", "sweet", "swift", "tall", "tame", "tender", "tidy", "tiny", "topaz", "tough", "tricky",
        "trusty", "urban", "vast", "velvet", "vivid", "warm", "wavy", "wide", "wild", "windy", "wise", "witty",
        "woolly", "young", "zesty"
    ];

    /// <summary>Things: everyday nouns, inoffensive.</summary>
    public static readonly IReadOnlyList<string> Things =
    [
        "acorn", "anchor", "apple", "apricot", "arrow", "autumn", "badger", "bagel", "bamboo", "banana", "banjo",
        "barrel", "basil", "beacon", "beetle", "berry", "biscuit", "blossom", "boulder", "breeze", "brick", "bridge",
        "bucket", "buffalo", "butter", "button", "cactus", "camel", "candle", "canoe", "canyon", "carrot", "castle",
        "cedar", "cheetah", "cherry", "chess", "cider", "cinnamon", "citrus", "clover", "cobalt", "coconut", "comet",
        "compass", "cookie", "copper", "coral", "cotton", "cougar", "crayon", "cricket", "crystal", "cupcake",
        "daisy", "dolphin", "donkey", "dragon", "drum", "eagle", "easel", "ember", "falcon", "feather", "fern",
        "fiddle", "finch", "fjord", "flamingo", "forest", "fossil", "fountain", "fox", "galaxy", "garden", "garlic",
        "gecko", "ginger", "giraffe", "glacier", "globe", "goose", "granite", "grape", "gravel", "guitar", "hammock",
        "harbour", "harp", "hazel", "hedgehog", "helmet", "heron", "hippo", "honey", "horizon", "iceberg", "igloo",
        "island", "ivory", "jackal", "jasmine", "jelly", "jigsaw", "jungle", "kayak", "kettle", "kiwi", "koala",
        "ladder", "lagoon", "lantern", "lemur", "lily", "lion", "llama", "lobster", "locket", "lotus", "mango",
        "maple", "marble", "meadow", "melon", "meteor", "mint", "mitten", "monkey", "moose", "mosaic", "muffin",
        "nectar", "nutmeg", "oasis", "ocean", "olive", "onion", "orbit", "orchid", "otter", "owl", "paddle", "palm",
        "panda", "papaya", "parrot", "peach", "peanut", "pebble", "pelican", "pepper", "piano", "pickle", "pigeon",
        "pillow", "pine", "pirate", "planet", "plum", "pond", "poppy", "potato", "pretzel", "puffin", "pumpkin",
        "quartz", "quill", "rabbit", "radish", "rainbow", "raven", "reef", "rhino", "ribbon", "river", "robin",
        "rocket", "saddle", "saffron", "salmon", "sandal", "sapphire", "satchel", "scarf", "seal", "sequoia",
        "shadow", "shell", "sparrow", "spider", "sponge", "spruce", "squirrel", "starfish", "stone", "summit",
        "sunset", "swan", "tablet", "teapot", "thistle", "thunder", "tiger", "toffee", "tomato", "topaz", "tortoise",
        "trumpet", "tulip", "turnip", "turtle", "umbrella", "valley", "vanilla", "velvet", "violin", "volcano",
        "waffle", "walnut", "walrus", "wave", "whale", "willow", "window", "winter", "wizard", "wombat", "yacht",
        "zebra"
    ];

    public static string Make(PassphraseOptions options)
    {
        var words = Math.Clamp(options.Words, MinWords, MaxWords);
        var parts = new System.Text.StringBuilder();
        parts.Append(Capital(Describing[RandomNumberGenerator.GetInt32(Describing.Count)]));
        for (var i = 1; i < words; i++)
            parts.Append(Capital(Things[RandomNumberGenerator.GetInt32(Things.Count)]));
        if (options.Number)
            parts.Append(RandomNumberGenerator.GetInt32(10, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (options.Symbol)
            parts.Append(SymbolSet[RandomNumberGenerator.GetInt32(SymbolSet.Length)]);
        return parts.ToString();
    }

    /// <summary>
    /// Roughly how hard it is to guess, in bits, counting someone who knows exactly how it's made and has these
    /// lists: that's what makes it honest, as the words themselves are easy to try.
    /// </summary>
    public static int Bits(PassphraseOptions options)
    {
        var words = Math.Clamp(options.Words, MinWords, MaxWords);
        var bits = Math.Log2(Describing.Count) + (words - 1) * Math.Log2(Things.Count);
        if (options.Number)
            bits += Math.Log2(90);
        if (options.Symbol)
            bits += Math.Log2(SymbolSet.Length);
        return (int)Math.Floor(bits);
    }

    /// <summary>What a word password of this strength is good for, in plain words.</summary>
    public static string Strength(int bits) => bits switch
    {
        < 30 => "Easy to guess: only for a site that locks you out after a few wrong tries",
        < 45 => "Fair: fine for most website logins",
        < 60 => "Strong",
        _ => "Very strong",
    };

    private static string Capital(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}
