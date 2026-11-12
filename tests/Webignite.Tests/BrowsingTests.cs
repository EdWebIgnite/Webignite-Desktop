using Webignite.Core.Web;

namespace Webignite.Tests;

public class BrowsingTests
{
    [Theory]
    [InlineData("karoocoffee.co.za", "https://karoocoffee.co.za/")]
    [InlineData("https://webignite.co.za/contact", "https://webignite.co.za/contact")]
    [InlineData("karoo coffee menu", "https://www.google.com/search?q=karoo%20coffee%20menu")]
    [InlineData("weather", "https://www.google.com/search?q=weather")]
    public void What_is_typed_is_an_address_or_a_search(string typed, string opens) =>
        Assert.Equal(opens, Browsing.Destination(typed)!.AbsoluteUri);

    [Fact]
    public void Nothing_typed_goes_nowhere() => Assert.Null(Browsing.Destination("  "));

    private const string History = """{"currentIndex":1,"entries":[{"id":4,"url":"https://a.example/"},{"id":7,"url":"https://b.example/"},{"id":9,"url":"https://c.example/"}]}""";

    [Fact]
    public void Back_and_forward_step_through_where_the_browser_has_been()
    {
        Assert.Equal(4, Browsing.HistoryStep(History, -1));
        Assert.Equal(9, Browsing.HistoryStep(History, 1));
        Assert.Equal("{\"entryId\":4}", Browsing.GoToMessage(4));
    }

    [Fact]
    public void At_either_end_or_with_an_answer_that_cant_be_read_there_is_nowhere_to_go()
    {
        Assert.Null(Browsing.HistoryStep(History, 2));
        Assert.Null(Browsing.HistoryStep(History, -2));
        Assert.Null(Browsing.HistoryStep("{}", -1));
        Assert.Null(Browsing.HistoryStep("not json", -1));
    }
}
