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

    [Fact]
    public void A_crop_dragged_on_a_zoomed_picture_is_in_the_pictures_own_pixels_and_stays_inside_it()
    {
        var area = PictureEditing.Crop(100, 50, 20, 250, zoom: 0.5, width: 800, height: 600);
        Assert.Equal(new Webignite.Core.Tools.ScreenGrab.Area(40, 100, 160, 400), area);
        Assert.Equal(new Webignite.Core.Tools.ScreenGrab.Area(0, 0, 800, 600), PictureEditing.Crop(-50, -50, 5000, 5000, 1, 800, 600));
        Assert.True(PictureEditing.IsCrop(area, 800, 600));
        Assert.False(PictureEditing.IsCrop(new(0, 0, 800, 600), 800, 600));
        Assert.False(PictureEditing.IsCrop(new(10, 10, 2, 2), 800, 600));
    }

    [Fact]
    public void Resizing_keeps_the_shape_from_the_side_typed_and_refuses_silly_sizes()
    {
        Assert.Equal((1920, 1440, (string?)null), PictureEditing.Resize(4032, 3024, 1920, null, keepShape: true));
        Assert.Equal((1000, 750, (string?)null), PictureEditing.Resize(4032, 3024, 1000, 999, keepShape: true));
        Assert.Equal((1000, 999, (string?)null), PictureEditing.Resize(4032, 3024, 1000, 999, keepShape: false));
        Assert.NotNull(PictureEditing.Resize(4032, 3024, null, null, true).Problem);
        Assert.NotNull(PictureEditing.Resize(4032, 3024, 0, null, true).Problem);
        Assert.NotNull(PictureEditing.Resize(4032, 3024, 50000, null, true).Problem);
        Assert.Equal(1440, PictureEditing.OtherSide(1920, 4032, 3024));
    }

    [Fact]
    public void A_converted_copy_goes_beside_the_original_and_never_over_it()
    {
        Assert.Equal(System.IO.Path.Combine("Shots", "logo.webp"), Webignite.Core.Tools.ImageRules.ConvertedPath(System.IO.Path.Combine("Shots", "logo.png"), Webignite.Core.Tools.ImageFormat.WebP, _ => false));
        var taken = System.IO.Path.Combine("Shots", "logo.webp");
        Assert.Equal(System.IO.Path.Combine("Shots", "logo (2).webp"), Webignite.Core.Tools.ImageRules.ConvertedPath(System.IO.Path.Combine("Shots", "logo.png"), Webignite.Core.Tools.ImageFormat.WebP, p => p == taken));
        Assert.Equal(System.IO.Path.Combine("Shots", "photo (2).png"), Webignite.Core.Tools.ImageRules.ConvertedPath(System.IO.Path.Combine("Shots", "photo.png"), Webignite.Core.Tools.ImageFormat.Png, _ => false));
    }

    [Fact]
    public void Recent_colours_keep_the_newest_first_once_each()
    {
        IReadOnlyList<string> recent = [];
        recent = Webignite.Core.Tools.Colours.Remember(recent, "#e8590c");
        recent = Webignite.Core.Tools.Colours.Remember(recent, "#1C1D21");
        recent = Webignite.Core.Tools.Colours.Remember(recent, "#E8590C");
        Assert.Equal(["#E8590C", "#1C1D21"], recent);
        Assert.Same(recent, Webignite.Core.Tools.Colours.Remember(recent, "not a colour"));
    }
}
