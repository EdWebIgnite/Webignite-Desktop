using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class ImageRulesTests
{
    [Theory]
    [InlineData(4000, 3000, 1920, 0, 1920, 1440)]
    [InlineData(800, 600, 1920, 0, 800, 600)]
    [InlineData(3000, 4000, 1920, 1080, 810, 1080)]
    [InlineData(5000, 100, 0, 0, 5000, 100)]
    public void Images_fit_and_never_grow(int w, int h, int maxW, int maxH, int newW, int newH) =>
        Assert.Equal((newW, newH), ImageRules.Fit(w, h, maxW, maxH));

    [Fact]
    public void Output_goes_in_a_web_folder_with_a_clean_name()
    {
        var source = Path.Combine("C:", "Clients", "Karoo", "Shop Front (1).JPG");
        var expected = Path.Combine("C:", "Clients", "Karoo", "web", "shop-front-1.webp");
        Assert.Equal(expected, ImageRules.OutputPath(source, ImageFormat.WebP, _ => false));
        var taken = new HashSet<string> { expected };
        Assert.EndsWith("shop-front-1-2.webp", ImageRules.OutputPath(source, ImageFormat.WebP, taken.Contains));
        Assert.Equal("cafe-menu", ImageRules.Slug("Café   Menu!"));
        Assert.Equal("image", ImageRules.Slug("___"));
    }

    [Fact]
    public void Same_format_keeps_the_kind()
    {
        Assert.Equal(ImageFormat.Jpeg, ImageRules.Output(ImageFormat.Same, "a.JPEG"));
        Assert.Equal(ImageFormat.Png, ImageRules.Output(ImageFormat.Same, "a.gif"));
        Assert.Equal(ImageFormat.WebP, ImageRules.Output(ImageFormat.WebP, "a.png"));
    }

    [Fact]
    public void Savings_read_plainly()
    {
        Assert.Equal("2.4 MB to 310 KB, 87% smaller", ImageRules.Saving(2_516_582, 317_440));
        Assert.StartsWith("40 KB to 41 KB, no smaller", ImageRules.Saving(40_960, 41_984));
    }
}
