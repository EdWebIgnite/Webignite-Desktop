using Webignite.Core.Layout;

namespace Webignite.Tests;

public class WidgetLooksTests
{
    private static MyPage PageWith(WidgetKind kind) => MyPages.Add(new MyPage { Id = "p", Name = "Morning", Grid = MyPages.Columns }, kind);

    [Fact]
    public void A_widget_is_given_a_colour_and_a_way_of_wearing_it()
    {
        var page = PageWith(WidgetKind.Mail);
        var coloured = MyPages.SetLook(page, page.Widgets[0].Id, "teal", WidgetTint.Heading);
        Assert.Equal("teal", coloured.Widgets[0].Colour);
        Assert.Equal(WidgetTint.Heading, coloured.Widgets[0].Tint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#ff0000")]
    public void No_colour_or_one_not_on_the_list_leaves_it_plain(string? colour)
    {
        var page = PageWith(WidgetKind.Note);
        Assert.Null(MyPages.SetLook(page, page.Widgets[0].Id, colour, WidgetTint.Wash).Widgets[0].Colour);
    }

    [Fact]
    public void A_wash_is_faint_and_never_the_colour_at_full_strength()
    {
        foreach (var tint in new[] { WidgetTint.Wash, WidgetTint.Heading })
        {
            var shade = WidgetLooks.Shade(tint);
            Assert.InRange(shade.Body, 0, 0.12);
            Assert.InRange(shade.Head, 0.1, 0.25);
        }
        Assert.True(WidgetLooks.Shade(WidgetTint.Wash).Body > 0);
        Assert.Equal(0, WidgetLooks.Shade(WidgetTint.Heading).Body);
        Assert.Equal(new WidgetShade(0, 0, 0, 1), WidgetLooks.Shade(WidgetTint.Line));
    }

    [Fact]
    public void Mixing_moves_the_surface_towards_the_colour_by_the_share_given()
    {
        Assert.Equal(255, WidgetLooks.Mix(255, 0, 0));
        Assert.Equal(0, WidgetLooks.Mix(255, 0, 1));
        Assert.Equal(230, WidgetLooks.Mix(255, 5, 0.1));
        Assert.Equal(52, WidgetLooks.Mix(30, 250, 0.1));
    }

    [Fact]
    public void A_clients_mail_widget_is_picked_for_a_client_as_the_client_widget_is()
    {
        Assert.True(MyPages.NeedsTarget(WidgetKind.ClientMail));
        Assert.Equal("A client's mail", MyPages.Label(WidgetKind.ClientMail));
        Assert.Equal(MyPages.StartSize(WidgetKind.Mail), MyPages.StartSize(WidgetKind.ClientMail));
    }
}
