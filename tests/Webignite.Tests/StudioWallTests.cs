using Webignite.Core.Studio;

namespace Webignite.Tests;

public sealed class StudioWallTests
{
    [Theory]
    [InlineData("Bosveld Builders", "BB")]
    [InlineData("G3", "G3")]
    [InlineData("Trimax", "T")]
    [InlineData("Kemin Industries SA (Pty) Ltd", "KI")]
    [InlineData("  ", "?")]
    public void Initials_come_from_the_first_two_words(string name, string initials) => Assert.Equal(initials, StudioWall.Initials(name));

    [Fact]
    public void There_is_a_card_for_each_client_heaviest_first_with_trouble_listed_first_on_its_back()
    {
        var stars = new[]
        {
            new Star("c1", StarKind.Client, "Small Co", null, 2, StarState.Quiet, "1 website"),
            new Star("s1", StarKind.Site, "small.co.za", "c1", 1, StarState.Quiet, "Website is up"),
            new Star("c2", StarKind.Client, "Big Co", null, 9, StarState.Problem, "6 things"),
            new Star("p1", StarKind.Project, "New website", "c2", 1, StarState.Busy, "3 tasks open"),
            new Star("p2", StarKind.Project, "Logo", "c2", 1, StarState.Quiet, "Nothing open"),
            new Star("s2", StarKind.Site, "big.co.za", "c2", 1, StarState.Problem, "Website is down"),
            new Star("r1", StarKind.Domain, "big.co.za", "c2", 1, StarState.Quiet, "Renews 1 Mar 2027"),
            new Star("r2", StarKind.Domain, "big.com", "c2", 1, StarState.Quiet, "Renews 1 Apr 2027"),
        };

        var cards = StudioWall.Build(stars);

        Assert.Equal(["Big Co", "Small Co"], cards.Select(c => c.Client.Name));
        Assert.Equal("Needs attention", cards[0].Status);
        Assert.Equal("big.co.za", cards[0].Site);
        Assert.Equal(["s2", "p1"], cards[0].Busy.Take(2).Select(s => s.Id));
        Assert.Equal(4, cards[0].Busy.Count);
        Assert.Equal(1, cards[0].More);
        Assert.Equal("All quiet", cards[1].Status);
        Assert.Equal(0, cards[1].More);
    }

    [Fact]
    public void A_clients_colour_is_the_same_every_time()
    {
        Assert.Equal(StudioWall.ColourOf("c1"), StudioWall.ColourOf("c1"));
        Assert.StartsWith("Project", StudioWall.ColourOf("anything"), StringComparison.Ordinal);
        Assert.True(Enumerable.Range(0, 40).Select(i => StudioWall.ColourOf("client" + i)).Distinct().Count() >= 6);
    }

    [Fact]
    public void The_demo_studio_makes_a_full_wall()
    {
        var cards = StudioWall.Build(StudioDemo.Stars(new DateOnly(2026, 11, 14)));

        Assert.Equal(22, cards.Count);
        Assert.Equal("Modic Digital", cards[0].Client.Name);
        Assert.Equal("MD", cards[0].Initials);
    }
}
