using Webignite.Core.Layout;
using Webignite.Core.Tools;

namespace Webignite.Tests;

public class WidgetLooksTests
{
    private static readonly Rgb White = new(255, 255, 255);
    private static readonly Rgb DarkSurface = new(30, 32, 36);
    private static readonly Rgb Ink = new(31, 42, 38);
    private static readonly Rgb PaleInk = new(232, 236, 234);

    private static MyPage PageWith(WidgetKind kind) => MyPages.Add(new MyPage { Id = "p", Name = "Morning", Grid = MyPages.Columns }, kind);

    [Fact]
    public void A_widgets_heading_body_and_line_each_take_a_colour_of_their_own()
    {
        var page = PageWith(WidgetKind.Mail);
        var coloured = MyPages.SetLook(page, page.Widgets[0].Id, new TileLook { Head = "#c6e3f7", Body = "fdddbe", Line = "teal", Strength = 44 });
        Assert.Equal(new TileLook { Head = "#C6E3F7", Body = "#FDDDBE", Line = "teal", Strength = 40 }, coloured.Widgets[0].Look);
        Assert.Equal(coloured.Widgets[0].Look, WidgetLooks.Of(coloured.Widgets[0]));
    }

    [Fact]
    public void A_look_with_nothing_coloured_is_no_look()
    {
        var page = PageWith(WidgetKind.Mail);
        Assert.Null(MyPages.SetLook(page, page.Widgets[0].Id, new TileLook { Head = "sky blue" }).Widgets[0].Look);
        Assert.Null(MyPages.SetLook(page, page.Widgets[0].Id, null).Widgets[0].Look);
        Assert.Null(TileLook.Tidied(new TileLook()));
    }

    [Fact]
    public void A_widget_coloured_before_the_parts_could_be_picked_apart_keeps_what_it_coloured()
    {
        Widget Old(WidgetTint tint, int strength) => new() { Id = "w", Kind = WidgetKind.Note, Colour = "#C6E3F7", Tint = tint, Strength = strength };
        Assert.Equal(new TileLook { Head = "#C6E3F7", Body = "#C6E3F7", Strength = 70 }, WidgetLooks.Of(Old(WidgetTint.Wash, 70)));
        Assert.Equal(new TileLook { Head = "#C6E3F7", Strength = 50 }, WidgetLooks.Of(Old(WidgetTint.Heading, 50)));
        Assert.Equal(new TileLook { Line = "#C6E3F7", Strength = 70 }, WidgetLooks.Of(Old(WidgetTint.Line, 70)));
        // The very first, a bright category colour as a faint wash, stays faint.
        Assert.Equal(WidgetLooks.LeastStrength, WidgetLooks.Of(new Widget { Id = "w", Kind = WidgetKind.Note, Colour = "blue" })!.Strength);
        Assert.Null(WidgetLooks.Of(new Widget { Id = "w", Kind = WidgetKind.Note }));
    }

    [Theory]
    [InlineData("#C6E3F7", "#C6E3F7")]
    [InlineData("c6e3f7", "#C6E3F7")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("teal", "teal")]
    public void A_code_typed_any_usual_way_or_a_category_colours_name_is_kept(string typed, string kept) => Assert.Equal(kept, WidgetLooks.Known(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sky blue")]
    [InlineData("#12345")]
    [InlineData("rgb(1,2,3)")]
    public void Anything_else_is_not_a_colour(string? colour) => Assert.Null(WidgetLooks.Known(colour));

    [Fact]
    public void Every_pastel_offered_is_light_soft_and_readable_at_full_strength()
    {
        Assert.True(WidgetLooks.Pastels.Count >= 16);
        Assert.Equal(WidgetLooks.Pastels.Count, WidgetLooks.Pastels.Select(p => p.Hex).Distinct().Count());
        foreach (var pastel in WidgetLooks.Pastels)
        {
            var colour = WidgetLooks.Code(pastel.Hex)!.Value;
            var (_, _, lightness) = Colours.ToHsl(colour);
            Assert.True(lightness >= 0.8, $"{pastel.Name} is too dark for a pastel");
            Assert.True(Colours.Contrast(colour, Ink) >= WidgetLooks.LeastContrast, $"{pastel.Name} can't be read on");
        }
    }

    [Fact]
    public void Intensity_runs_from_ten_to_a_hundred_in_tens()
    {
        Assert.Equal(10, WidgetLooks.Strength(-5));
        Assert.Equal(100, WidgetLooks.Strength(140));
        Assert.Equal(60, WidgetLooks.Strength(57));
    }

    [Fact]
    public void A_stronger_intensity_shows_more_of_the_colour()
    {
        Assert.Equal(0.2, WidgetLooks.Share(20), 3);
        Assert.Equal(1, WidgetLooks.Share(100), 3);
        Assert.True(WidgetLooks.Share(70) > WidgetLooks.Share(30));
    }

    [Fact]
    public void A_pastel_is_used_in_full_on_a_light_look_and_held_back_on_a_dark_one()
    {
        var sky = WidgetLooks.Code("#C6E3F7")!.Value;
        Assert.Equal(1, WidgetLooks.Readable(White, sky, Ink, 1));
        var onDark = WidgetLooks.Readable(DarkSurface, sky, PaleInk, 1);
        Assert.InRange(onDark, 0.05, 0.6);
        Assert.True(Colours.Contrast(WidgetLooks.Mix(DarkSurface, sky, onDark), PaleInk) >= WidgetLooks.LeastContrast);
    }

    [Fact]
    public void A_dark_colour_typed_in_is_held_back_to_where_the_writing_can_be_read()
    {
        var navy = new Rgb(20, 20, 90);
        var share = WidgetLooks.Readable(White, navy, Ink, 1);
        Assert.True(share < 0.5);
        Assert.True(Colours.Contrast(WidgetLooks.Mix(White, navy, share), Ink) >= WidgetLooks.LeastContrast);
    }

    [Fact]
    public void Mixing_moves_the_surface_towards_the_colour_by_the_share_given()
    {
        Assert.Equal(255, WidgetLooks.Mix((byte)255, (byte)0, 0));
        Assert.Equal(0, WidgetLooks.Mix((byte)255, (byte)0, 1));
        Assert.Equal(230, WidgetLooks.Mix((byte)255, (byte)5, 0.1));
        Assert.Equal(new Rgb(128, 128, 128), WidgetLooks.Mix(new Rgb(0, 0, 0), new Rgb(255, 255, 255), 0.5));
    }

    [Fact]
    public void A_clients_mail_widget_is_picked_for_a_client_as_the_client_widget_is()
    {
        Assert.True(MyPages.NeedsTarget(WidgetKind.ClientMail));
        Assert.Equal("A client's mail", MyPages.Label(WidgetKind.ClientMail));
        Assert.Equal(MyPages.StartSize(WidgetKind.Mail), MyPages.StartSize(WidgetKind.ClientMail));
    }
}
