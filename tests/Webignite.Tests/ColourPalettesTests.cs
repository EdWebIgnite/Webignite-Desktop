using Webignite.Core.Tools;

namespace Webignite.Tests;

public class ColourPalettesTests
{
    private static readonly Rgb Orange = new(0xE8, 0x59, 0x0C);

    [Fact]
    public void A_scale_runs_from_nearly_white_through_the_colour_to_nearly_black()
    {
        var scale = ColourPalettes.Scale(Orange);
        Assert.Equal([50, 100, 200, 300, 400, 500, 600, 700, 800, 900], scale.Select(s => s.Number));
        Assert.Equal(Orange, scale[5].Colour);
        var light = scale.Select(s => Colours.ToHsl(s.Colour).L).ToList();
        Assert.Equal(light.OrderByDescending(l => l), light);
        Assert.True(light[0] > 0.9 && light[^1] < 0.25);
    }

    [Fact]
    public void Mixing_goes_from_one_colour_to_the_other()
    {
        Assert.Equal(Orange, ColourPalettes.Mix(Orange, new Rgb(255, 255, 255), 0));
        Assert.Equal(new Rgb(255, 255, 255), ColourPalettes.Mix(Orange, new Rgb(255, 255, 255), 1));
        Assert.Equal(new Rgb(128, 128, 128), ColourPalettes.Mix(new Rgb(0, 0, 0), new Rgb(255, 255, 255), 0.5));
    }

    [Fact]
    public void The_colours_that_go_with_one_are_turns_round_the_wheel()
    {
        var with = ColourPalettes.GoesWith(new Rgb(255, 0, 0));
        Assert.Equal(("Opposite", new Rgb(0, 255, 255)), with[0]);
        Assert.Equal(new Rgb(0, 255, 0), with[3].Colour);
        Assert.Equal(new Rgb(0, 0, 255), with[4].Colour);
        Assert.Equal(5, with.Count);
    }

    [Theory]
    [InlineData(255, 255, 255, "cmyk(0%, 0%, 0%, 0%)")]
    [InlineData(0, 0, 0, "cmyk(0%, 0%, 0%, 100%)")]
    [InlineData(255, 0, 0, "cmyk(0%, 100%, 100%, 0%)")]
    [InlineData(0xE8, 0x59, 0x0C, "cmyk(0%, 62%, 95%, 9%)")]
    public void Colours_are_given_for_print(byte r, byte g, byte b, string cmyk) => Assert.Equal(cmyk, ColourPalettes.Cmyk(new Rgb(r, g, b)));

    [Fact]
    public void Text_on_a_background_is_judged_by_the_accessibility_rules()
    {
        var black = Colours.Contrast(new Rgb(0, 0, 0), new Rgb(255, 255, 255));
        Assert.Equal("21.0 to 1", ColourPalettes.Ratio(black));
        Assert.All(ColourPalettes.Verdicts(black), v => Assert.True(v.Passes));
        // White on the brand orange: fine for headings, not for body text.
        var onOrange = Colours.Contrast(new Rgb(255, 255, 255), Orange);
        Assert.Equal([false, true, true, false], ColourPalettes.Verdicts(onOrange).Select(v => v.Passes));
        Assert.All(ColourPalettes.Verdicts(1.2), v => Assert.False(v.Passes));
    }

    [Fact]
    public void A_pictures_main_colours_come_out_most_used_first_with_near_shades_as_one()
    {
        var pixels = new List<uint>();
        pixels.AddRange(Enumerable.Repeat(0xFFFFFFFFu, 500));   // white
        pixels.AddRange(Enumerable.Repeat(0xFFE8590Cu, 300));   // orange
        pixels.AddRange(Enumerable.Repeat(0xFFE65A10u, 100));   // nearly the same orange
        pixels.AddRange(Enumerable.Repeat(0xFF3D2B1Fu, 200));   // brown
        pixels.AddRange(Enumerable.Repeat(0x000000FFu, 900));   // see-through: not there at all

        var colours = ColourPalettes.FromPixels(pixels.ToArray());

        Assert.Equal(3, colours.Count);
        Assert.Equal(new Rgb(255, 255, 255), colours[0]);
        Assert.True(Math.Abs(colours[1].R - 0xE8) < 4 && Math.Abs(colours[1].G - 0x59) < 4);
        Assert.Equal(new Rgb(0x3D, 0x2B, 0x1F), colours[2]);
        Assert.Empty(ColourPalettes.FromPixels([]));
        Assert.Equal(2, ColourPalettes.FromPixels(pixels.ToArray(), count: 2).Count);
    }

    [Fact]
    public void An_svgs_colours_are_read_from_its_code_each_once()
    {
        const string logo = """<svg><rect fill="#E8590C"/><path fill="#e8590c" stroke="#fff"/><circle style="fill: rgb(61, 43, 31)"/><text fill="url(#grad)">x</text></svg>""";
        Assert.Equal([Orange, new Rgb(255, 255, 255), new Rgb(61, 43, 31)], ColourPalettes.FromSvg(logo));
        Assert.Empty(ColourPalettes.FromSvg("<svg/>"));
    }

    [Fact]
    public void Colours_are_added_once_and_named_in_turn()
    {
        var palette = ColourPalettes.Add([], Orange);
        palette = ColourPalettes.Add(palette, Orange);
        palette = ColourPalettes.Add(palette, new Rgb(0, 0, 0), " Ink ");
        palette = ColourPalettes.Add(palette, new Rgb(255, 255, 255));
        Assert.Equal([new Swatch("colour-1", "#E8590C"), new Swatch("Ink", "#000000"), new Swatch("colour-3", "#FFFFFF")], palette);

        var full = palette;
        for (var i = 0; i < 40; i++)
            full = ColourPalettes.Add(full, new Rgb((byte)(i * 5), 10, 10));
        Assert.Equal(ColourPalettes.Most, full.Count);
    }

    [Fact]
    public void The_palette_is_written_as_css_variables()
    {
        IReadOnlyList<Swatch> palette = [new("Primary", "#e8590c"), new("Dark Brown!", "#3D2B1F"), new("primary", "#FFFFFF"), new("  ", "#000000")];
        Assert.Equal(":root {\n  --primary: #E8590C;\n  --dark-brown: #3D2B1F;\n  --primary-2: #FFFFFF;\n  --colour: #000000;\n}", ColourPalettes.CssVariables(palette));
        Assert.Equal("Primary #E8590C" + Environment.NewLine + "Dark Brown! #3D2B1F", ColourPalettes.Listed([.. palette.Take(2)]));
    }
}
