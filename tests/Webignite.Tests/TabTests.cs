using Webignite.Core.Layout;

namespace Webignite.Tests;

public sealed class TabTests
{
    private static readonly HashSet<string> Known = ["Overview", "Mail", "Clients"];

    [Fact]
    public void Saved_tabs_come_back_with_the_one_that_was_showing()
    {
        var back = TabRules.Restore(new TabSettings { Pages = ["Mail", "Clients", "Mail"], Active = 2 }, Known, "Overview");
        Assert.Equal(["Mail", "Clients", "Mail"], back.Pages);
        Assert.Equal(2, back.Active);
    }

    [Fact]
    public void A_page_this_version_no_longer_has_is_dropped_and_the_active_one_follows()
    {
        var back = TabRules.Restore(new TabSettings { Pages = ["Gone", "Mail", "Clients"], Active = 2 }, Known, "Overview");
        Assert.Equal(["Mail", "Clients"], back.Pages);
        Assert.Equal(1, back.Active);
    }

    [Fact]
    public void Nothing_saved_starts_on_the_home_page()
    {
        Assert.Equal(["Overview"], TabRules.Restore(new TabSettings(), Known, "Overview").Pages);
        Assert.Equal(["Overview"], TabRules.Restore(new TabSettings { Pages = ["Gone"], Active = 4 }, Known, "Overview").Pages);
    }

    [Theory]
    [InlineData(0, 3, 1, 1)]
    [InlineData(2, 3, 1, 0)]
    [InlineData(0, 3, -1, 2)]
    [InlineData(0, 0, 1, 0)]
    public void Ctrl_tab_goes_round(int index, int count, int by, int expected) => Assert.Equal(expected, TabRules.Step(index, count, by));

    [Theory]
    [InlineData(1, 3, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(0, 0, 0)]
    public void After_closing_the_next_tab_along_shows(int closed, int countAfter, int expected) =>
        Assert.Equal(expected, TabRules.AfterClose(closed, countAfter));

    [Fact]
    public void Popped_out_windows_come_back_where_they_were_unless_their_page_is_gone()
    {
        var back = TabRules.Restore(new TabSettings
        {
            Pages = ["Overview"],
            PoppedOut = [new("Mail", 1920, 40, 900, 700), new("Gone", 0, 0, 800, 600), new("Clients", 10, 10, 0, 0)],
        }, Known, "Overview");
        Assert.Equal([new PopOutPlacement("Mail", 1920, 40, 900, 700)], back.PoppedOut);
    }
}
