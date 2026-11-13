using Webignite.Core.Layout;

namespace Webignite.Tests;

public sealed class ShortcutListTests
{
    private static readonly Shortcut[] All =
    [
        new("Ctrl+K", "Search everything", ShortcutList.Everywhere),
        new("F5", "Read this page again", ShortcutList.Everywhere),
        new("E", "Archive", ShortcutList.Mail),
        new("Ctrl+Shift+N", "New folder", ShortcutList.Files),
        new("F2, Delete", "Rename, or send to the Recycle Bin", ShortcutList.Files),
    ];

    [Fact]
    public void Shortcuts_are_listed_under_their_headings_in_the_order_given()
    {
        var shown = ShortcutList.Show(All);
        Assert.Equal([ShortcutList.Everywhere, ShortcutList.Mail, ShortcutList.Files], shown.Select(g => g.Name));
        Assert.Equal(["Ctrl+Shift+N", "F2, Delete"], shown[2].Items.Select(s => s.Keys));
        Assert.Equal(5, ShortcutList.Count(shown));
    }

    [Fact]
    public void The_page_that_is_open_comes_first()
    {
        Assert.Equal([ShortcutList.Files, ShortcutList.Everywhere, ShortcutList.Mail], ShortcutList.Show(All, first: ShortcutList.Files).Select(g => g.Name));
        // A page with no shortcuts of its own leaves the order alone.
        Assert.Equal(ShortcutList.Everywhere, ShortcutList.Show(All, first: ShortcutList.Notes)[0].Name);
    }

    [Theory]
    [InlineData("archive", "E")]
    [InlineData("ctrl k", "Ctrl+K")]
    [InlineData("CTRL+SHIFT", "Ctrl+Shift+N")]
    [InlineData("recycle", "F2, Delete")]
    [InlineData("files folder", "Ctrl+Shift+N")]
    public void Typing_finds_a_shortcut_by_its_keys_what_it_does_or_its_heading(string typed, string keys)
    {
        var shown = ShortcutList.Show(All, typed);
        Assert.Equal(keys, Assert.Single(Assert.Single(shown).Items).Keys);
    }

    [Fact]
    public void Typing_a_heading_shows_all_of_it_and_nothing_found_is_an_empty_list()
    {
        Assert.Equal(2, ShortcutList.Count(ShortcutList.Show(All, "files")));
        Assert.Empty(ShortcutList.Show(All, "zebra"));
    }
}
