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
    public void A_widget_is_given_a_colour_a_way_of_wearing_it_and_an_intensity()
    {
        var page = PageWith(WidgetKind.Mail);
        var coloured = MyPages.SetLook(page, page.Widgets[0].Id, "#c6e3f7", WidgetTint.Heading, 44);
        Assert.Equal("#C6E3F7", coloured.Widgets[0].Colour);
        Assert.Equal(WidgetTint.Heading, coloured.Widgets[0].Tint);
        Assert.Equal(40, coloured.Widgets[0].Strength);
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
    public void Anything_else_leaves_the_widget_plain(string? colour)
    {
        var page = PageWith(WidgetKind.Note);
        Assert.Null(MyPages.SetLook(page, page.Widgets[0].Id, colour, WidgetTint.Wash).Widgets[0].Colour);
    }

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
        var faint = WidgetLooks.Shade(WidgetTint.Wash, 20);
        var full = WidgetLooks.Shade(WidgetTint.Wash, 100);
        Assert.True(full.Body > faint.Body && full.Head > faint.Head);
        Assert.Equal(1, full.Head);
        Assert.True(full.Body < full.Head);
        Assert.Equal(0, WidgetLooks.Shade(WidgetTint.Heading, 100).Body);
        Assert.Equal(new WidgetShade(0, 0, 0, 1), WidgetLooks.Shade(WidgetTint.Line, 50));
    }

    [Fact]
    public void A_widget_coloured_before_intensities_keeps_its_faint_wash()
    {
        var shade = WidgetLooks.Shade(WidgetTint.Wash);
        Assert.InRange(shade.Body, 0.01, 0.12);
        Assert.InRange(shade.Head, 0.1, 0.25);
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
