using Webignite.Core.Writing;

namespace Webignite.Tests;

/// <summary>Spell check: which words are checked, which are marked, and which language is used.</summary>
public sealed class SpellingTests
{
    private sealed class Dictionary(params string[] words) : ISpelling
    {
        private readonly HashSet<string> known = new(words, StringComparer.OrdinalIgnoreCase);

        public string Language => "en-ZA";
        public int Revision { get; private set; }
        public bool IsWrong(string word) => !known.Contains(word);
        public IReadOnlyList<string> Suggest(string word) => [];

        public void Add(string word)
        {
            known.Add(word);
            Revision++;
        }

        public void Ignore(string word) => Add(word);
    }

    private static string[] Checked(string text) => [.. Spelling.Words(text).Select(w => w.Word)];

    [Fact]
    public void Addresses_numbers_codes_and_names_with_capitals_inside_are_not_checked()
    {
        Assert.Empty(Checked("www.webignite.co.za https://karoocoffee.co.za/shop thandi@karoocoffee.co.za invoice.pdf C:\\Clients"));
        Assert.Empty(Checked("R1 500 10am INV-0107 2026"));
        Assert.Empty(Checked("VAT PDF WordPress iPhone {name} {client} a"));
        Assert.Equal(["Hi", "Thandi", "the", "site's", "done", "follow", "up", "on", "Friday"], Checked("Hi Thandi, the site's done. Follow-up on Friday!")
            .Select(w => w == "Follow" ? "follow" : w));
    }

    [Fact]
    public void Each_misspelt_word_is_found_where_it_is()
    {
        var text = "Thanks for teh paymnet, Thandi.";
        var found = Spelling.Mistakes(text, new Dictionary("thanks", "for", "Thandi"));

        Assert.Equal(["teh", "paymnet"], found.Select(f => f.Word));
        Assert.Equal("teh", text.Substring(found[0].Start, found[0].Length));
        Assert.Equal("paymnet", text.Substring(found[1].Start, found[1].Length));
        Assert.Equal("paymnet", Spelling.At(text, found[1].Start + 3, new Dictionary("thanks", "for", "Thandi"))?.Word);
        Assert.Null(Spelling.At(text, 2, new Dictionary("thanks", "for", "Thandi")));
    }

    [Fact]
    public void The_word_being_typed_is_left_alone()
    {
        var text = "Thanks for the paym";
        var known = new Dictionary("thanks", "for", "the");

        Assert.Empty(Spelling.Mistakes(text, known, typingAt: text.Length));
        Assert.Single(Spelling.Mistakes(text, known));
    }

    [Fact]
    public void South_African_English_is_used_when_the_PC_has_it_then_British_then_American()
    {
        Assert.Equal("en-ZA", Spelling.Pick(null, ["en-US", "en-GB", "en-ZA"]));
        Assert.Equal("en-GB", Spelling.Pick(null, ["en-US", "en-GB", "af-ZA"]));
        Assert.Equal("en-US", Spelling.Pick("", ["af-ZA", "en-US"]));
        Assert.Equal("af-ZA", Spelling.Pick("AF-za", ["af-ZA", "en-US"]));
        Assert.Equal("en-GB", Spelling.Pick("xh-ZA", ["en-GB", "af-ZA"]));
        Assert.Null(Spelling.Pick(null, []));
    }
}
