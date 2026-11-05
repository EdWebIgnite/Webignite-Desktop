using Webignite.Core.Files;

namespace Webignite.Tests;

/// <summary>The picture viewer's fit, zoom steps and the order the arrow keys go through a folder.</summary>
public sealed class PictureViewingTests
{
    [Fact]
    public void A_big_picture_fits_the_window_and_a_small_one_shows_at_its_own_size()
    {
        Assert.Equal(0.25, PictureViewing.Fit(4000, 3000, 1000, 800));
        Assert.Equal(1, PictureViewing.Fit(300, 200, 1000, 800));
        Assert.Equal(1, PictureViewing.Fit(0, 0, 1000, 800));
    }

    [Fact]
    public void Zoom_goes_up_and_down_in_steps_and_stays_within_bounds()
    {
        Assert.Equal(1.25, PictureViewing.In(1));
        Assert.Equal(0.75, PictureViewing.Out(1));
        Assert.Equal(0.33, PictureViewing.In(0.3));
        Assert.Equal(PictureViewing.MaxZoom, PictureViewing.In(PictureViewing.MaxZoom));
        Assert.Equal(PictureViewing.MinZoom, PictureViewing.Out(PictureViewing.MinZoom));
        Assert.Equal("25%", PictureViewing.Percent(0.25));
    }

    [Fact]
    public void The_arrow_keys_go_through_the_folders_pictures_in_name_order_and_round()
    {
        var pictures = PictureViewing.Pictures([@"C:\Shots\photo 10.jpg", @"C:\Shots\brief.pdf", @"C:\Shots\Photo 2.PNG", @"C:\Shots\logo.webp"]);
        Assert.Equal([@"C:\Shots\logo.webp", @"C:\Shots\Photo 2.PNG", @"C:\Shots\photo 10.jpg"], pictures);
        Assert.Equal(0, PictureViewing.Step(2, +1, 3));
        Assert.Equal(2, PictureViewing.Step(0, -1, 3));
        Assert.Equal(-1, PictureViewing.Step(0, +1, 0));
    }

    [Fact]
    public void Under_the_picture_its_place_size_and_file_size_show()
    {
        Assert.StartsWith("3 of 12 · 4032 × 3024 · ", PictureViewing.Describe(2, 12, 4032, 3024, 2_200_000), StringComparison.Ordinal);
        Assert.Equal("800 × 600", PictureViewing.Describe(0, 1, 800, 600, 0));
    }
}
