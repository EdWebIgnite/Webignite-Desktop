using Webignite.Core.Tools;

namespace Webignite.Tests;

public class SnippetsTests
{
    private static readonly IReadOnlyList<Snippet> Kept =
    [
        new("1", "Banking details", "bank", "Webignite\nFNB cheque 62000000000\nBranch 250655"),
        new("2", "Thanks for the payment", "", "Hi\n\nThanks, payment received on {date}."),
        new("3", "Bankruptcy notice", "notice", "A notice."),
        new("4", "Hosting renewal", "host", "Your hosting renews soon. Our banking details are below."),
    ];

    [Fact]
    public void Nothing_typed_lists_them_all_in_their_own_order() => Assert.Equal(Kept, Snippets.Find(Kept, "  "));

    [Fact]
    public void A_code_typed_exactly_comes_first_then_names_that_start_with_it_then_the_rest()
    {
        Assert.Equal(["1", "3", "4"], Snippets.Find(Kept, "bank").Select(s => s.Id));
        Assert.Equal(["4"], Snippets.Find(Kept, "HOST").Select(s => s.Id));
        Assert.Equal(["2"], Snippets.Find(Kept, "payment").Select(s => s.Id));
        Assert.Empty(Snippets.Find(Kept, "zebra"));
    }

    [Fact]
    public void A_snippet_is_found_by_words_in_its_text() => Assert.Equal(["1"], Snippets.Find(Kept, "250655").Select(s => s.Id));

    [Fact]
    public void The_date_time_and_clipboard_are_filled_in_as_it_is_pasted()
    {
        Assert.Equal("Paid on 11 November 2026 at 14:05. Ref: INV-0042", Snippets.Fill("Paid on {date} at {TIME}. Ref: {clipboard}", "11 November 2026", "14:05", "INV-0042"));
        Assert.Equal("Ref: ", Snippets.Fill("Ref: {clipboard}", "", "", null));
        Assert.Equal("Plain text {other}", Snippets.Fill("Plain text {other}", "x", "y", "z"));
    }

    [Fact]
    public void A_snippet_needs_a_name_and_text_and_a_code_of_its_own()
    {
        Assert.Equal("Give it a name to find it by.", Snippets.Problem(Kept, null, " ", "", "Text"));
        Assert.Equal("There is no text to paste yet.", Snippets.Problem(Kept, null, "Name", "", " "));
        Assert.Equal("A code is one word, with no spaces.", Snippets.Problem(Kept, null, "Name", "two words", "Text"));
        Assert.Equal("Another snippet has the code \"BANK\" already.", Snippets.Problem(Kept, null, "Name", "BANK", "Text"));
        Assert.StartsWith("That is too long", Snippets.Problem(Kept, null, "Name", "", new string('a', Snippets.Longest + 1)));
        // Its own code is fine when it is being changed, and so is no code at all.
        Assert.Null(Snippets.Problem(Kept, "1", "Banking details", "bank", "Text"));
        Assert.Null(Snippets.Problem(Kept, null, "Name", "", "Text"));
        Assert.Null(Snippets.Problem(Kept, null, "Name", null, "Text"));
    }

    [Fact]
    public void Saving_adds_a_new_one_at_the_end_or_changes_the_one_it_is()
    {
        var added = Snippets.Save(Kept, new Snippet("5", " Signature ", " sig ", "Eddie"));
        Assert.Equal(5, added.Count);
        Assert.Equal(("Signature", "sig"), (added[4].Name, added[4].Code));
        var changed = Snippets.Save(Kept, Kept[1] with { Name = "Payment thanks" });
        Assert.Equal(4, changed.Count);
        Assert.Equal("Payment thanks", changed[1].Name);
        Assert.Equal(["1", "3", "4"], Snippets.Remove(Kept, "2").Select(s => s.Id));
    }

    [Fact]
    public void The_preview_is_the_first_line_cut_short()
    {
        Assert.Equal("Webignite …", Snippets.Preview(Kept[0].Text));
        Assert.Equal("A notice.", Snippets.Preview("  A notice.  "));
        Assert.Equal(new string('a', 70) + "…", Snippets.Preview(new string('a', 200)));
    }

    [Fact]
    public void Its_shortcut_cant_take_another_shortcuts_keys()
    {
        var shots = new ScreenshotSettings();
        var (keys, _) = Hotkey.Parse(shots.SnippetShortcut);
        Assert.Equal("Ctrl+Alt+T", shots.SnippetShortcut);
        Assert.Equal(GlobalShortcut.Snippets, GlobalShortcuts.TakenBy(keys!, shots, "Ctrl+Alt+N", GlobalShortcut.Ruler));
        Assert.Null(GlobalShortcuts.TakenBy(keys!, shots, "Ctrl+Alt+N", GlobalShortcut.Snippets));
    }
}
