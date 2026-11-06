using Webignite.Core.Assistants;

namespace Webignite.Tests;

public class AssistantLookupTests
{
    private sealed record Thing(string Id, string Name);

    private static readonly Thing[] Things = [new("a1", "Bosveld Builders website"), new("b2", "Karoo Coffee website"), new("c3", "Karoo shop")];

    [Theory]
    [InlineData("a1", "a1")]
    [InlineData(" bosveld builders WEBSITE ", "a1")]
    [InlineData("coffee", "b2")]
    public void A_thing_is_found_by_id_name_or_words_when_only_one_fits(string typed, string expected) =>
        Assert.Equal(expected, AssistantLookup.Find(Things, typed, t => t.Id, t => t.Name, "project").Found?.Id);

    [Fact]
    public void Several_or_none_say_so()
    {
        var (found, problem) = AssistantLookup.Find(Things, "karoo", t => t.Id, t => t.Name, "project");
        Assert.Null(found);
        Assert.Contains("Karoo Coffee website (b2)", problem);
        Assert.Contains("No project", AssistantLookup.Find(Things, "nothing", t => t.Id, t => t.Name, "project").Problem);
        Assert.Contains("Say which", AssistantLookup.Find(Things, " ", t => t.Id, t => t.Name, "project").Problem);
    }

    [Fact]
    public void Dates_are_read_or_cleared()
    {
        Assert.Equal((true, new DateOnly(2026, 11, 14), ""), AssistantLookup.Date("2026-11-14", "due"));
        Assert.Equal((true, (DateOnly?)null, ""), AssistantLookup.Date("none", "due"));
        Assert.False(AssistantLookup.Date(null, "due").Given);
        Assert.Contains("like 2026-11-14", AssistantLookup.Date("next week", "due").Problem);
    }
}
