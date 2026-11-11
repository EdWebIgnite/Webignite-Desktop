using Webignite.Core.Tools;

namespace Webignite.Tests;

public class SocialSizesTests
{
    private static SocialSize Named(string label) => SocialSizes.All.First(s => s.Label == label);

    [Fact]
    public void The_usual_networks_are_there_each_once()
    {
        foreach (var network in new[] { "Facebook", "Instagram", "LinkedIn", "WhatsApp", "Link" })
            Assert.Contains(SocialSizes.All, s => s.Network == network);
        Assert.Equal(SocialSizes.All.Count, SocialSizes.All.Select(s => s.Label).Distinct().Count());
        Assert.Equal("1080 × 1350", Named("Instagram portrait").Pixels);
        Assert.Contains(SocialSizes.All, s => s.Usual);
    }

    [Fact]
    public void A_wide_picture_loses_its_sides_for_a_square()
    {
        Assert.Equal((500, 0, 1000, 1000), SocialSizes.Crop(2000, 1000, Named("Instagram square"), SocialFocus.Centre));
        Assert.Equal((0, 0, 1000, 1000), SocialSizes.Crop(2000, 1000, Named("Instagram square"), SocialFocus.Left));
        Assert.Equal((1000, 0, 1000, 1000), SocialSizes.Crop(2000, 1000, Named("Instagram square"), SocialFocus.Right));
        // Top and bottom have nothing to choose between when only the sides are cut.
        Assert.Equal((500, 0, 1000, 1000), SocialSizes.Crop(2000, 1000, Named("Instagram square"), SocialFocus.Top));
    }

    [Fact]
    public void A_tall_picture_loses_its_top_or_bottom_for_a_wide_shape()
    {
        var post = Named("Facebook post");
        Assert.Equal((0, 737, 1000, 525), SocialSizes.Crop(1000, 2000, post, SocialFocus.Centre));
        Assert.Equal((0, 0, 1000, 525), SocialSizes.Crop(1000, 2000, post, SocialFocus.Top));
        Assert.Equal((0, 1475, 1000, 525), SocialSizes.Crop(1000, 2000, post, SocialFocus.Bottom));
    }

    [Fact]
    public void A_picture_already_the_right_shape_is_kept_whole()
    {
        Assert.Equal((0, 0, 2400, 1260), SocialSizes.Crop(2400, 1260, Named("Facebook post"), SocialFocus.Centre));
        Assert.Equal((0, 0, 1, 1), SocialSizes.Crop(1, 1, Named("LinkedIn page cover"), SocialFocus.Centre));
    }

    [Fact]
    public void A_picture_too_small_for_a_size_is_said_to_be_soft()
    {
        Assert.False(SocialSizes.Soft(3000, 2000, Named("Instagram story or reel"), SocialFocus.Centre));
        Assert.True(SocialSizes.Soft(800, 600, Named("Instagram story or reel"), SocialFocus.Centre));
        // A little short is fine; nobody sees a tenth.
        Assert.False(SocialSizes.Soft(1100, 1100, Named("Facebook post"), SocialFocus.Centre));
    }

    [Fact]
    public void Files_say_what_they_are_for()
    {
        Assert.Equal("karoo-opening-instagram-square-1080x1080.jpg", SocialSizes.FileName("Karoo opening", Named("Instagram square"), "jpg"));
        Assert.Equal("picture-link-preview-og-image-1200x630.png", SocialSizes.FileName(" ", Named("Link preview (og:image)"), "png"));
    }

    [Fact]
    public void Saving_says_how_many_and_which_may_be_soft()
    {
        Assert.Equal("6 pictures saved.", SocialSizes.Saved(6, 0));
        Assert.StartsWith("1 picture saved. One had to be enlarged", SocialSizes.Saved(1, 1));
        Assert.StartsWith("6 pictures saved. 2 had to be enlarged", SocialSizes.Saved(6, 2));
    }
}
