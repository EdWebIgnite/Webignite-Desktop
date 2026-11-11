using System.Text.Json;
using Webignite.Core.Web;

namespace Webignite.Tests;

public class PageCaptureTests
{
    private static readonly ShotWidth Phone = PageCapture.Widths[0];

    [Fact]
    public void Offers_a_phone_a_tablet_a_laptop_and_a_desktop_narrowest_first()
    {
        Assert.Equal(["Phone", "Tablet", "Laptop", "Desktop"], PageCapture.Widths.Select(w => w.Name));
        Assert.Equal([390, 768, 1366, 1920], PageCapture.Widths.Select(w => w.Width));
        Assert.Equal("Phone (390 px)", Phone.Label);
    }

    [Theory]
    [InlineData("karoocoffee.co.za", "https://karoocoffee.co.za/")]
    [InlineData(" http://karoocoffee.co.za/menu ", "http://karoocoffee.co.za/menu")]
    [InlineData("https://karoocoffee.co.za/menu#lunch", "https://karoocoffee.co.za/menu")]
    public void An_address_gets_https_when_left_off(string typed, string expected) =>
        Assert.Equal(expected, PageCapture.Address(typed)?.AbsoluteUri);

    [Theory]
    [InlineData("")]
    [InlineData("karoo coffee")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    public void What_isnt_a_web_address_is_refused(string typed) => Assert.Null(PageCapture.Address(typed));

    [Fact]
    public void A_picture_of_the_screen_is_as_tall_as_the_screen() =>
        Assert.Equal((844, false), PageCapture.Height(Phone, 5000, wholePage: false));

    [Fact]
    public void A_picture_of_the_whole_page_is_as_tall_as_the_page() =>
        Assert.Equal((3142, false), PageCapture.Height(Phone, 3141.2, wholePage: true));

    [Fact]
    public void A_short_page_still_fills_the_screen() =>
        Assert.Equal((844, false), PageCapture.Height(Phone, 300, wholePage: true));

    [Fact]
    public void A_very_long_page_is_cut_off_and_says_so()
    {
        Assert.Equal((PageCapture.Tallest, true), PageCapture.Height(Phone, 90000, wholePage: true));
        Assert.Equal((PageCapture.Tallest, false), PageCapture.Height(Phone, PageCapture.Tallest, wholePage: true));
    }

    [Theory]
    [InlineData("https://www.karoocoffee.co.za/", "karoocoffee-co-za")]
    [InlineData("https://karoocoffee.co.za/about/our-team/", "karoocoffee-co-za-about-our-team")]
    [InlineData("https://karoocoffee.co.za/menu.html?week=2", "karoocoffee-co-za-menu-html")]
    [InlineData("https://karoocoffee.co.za/caf%C3%A9%20menu", "karoocoffee-co-za-caf-menu")]
    public void Files_are_named_after_the_address(string address, string expected) =>
        Assert.Equal(expected, PageCapture.Name(new Uri(address)));

    [Fact]
    public void A_long_address_makes_a_short_name()
    {
        var name = PageCapture.Name(new Uri("https://karoocoffee.co.za/" + new string('a', 300)));
        Assert.True(name.Length <= 80);
    }

    [Fact]
    public void Pictures_and_pdfs_get_their_endings()
    {
        var page = new Uri("https://karoocoffee.co.za/menu");
        Assert.Equal("karoocoffee-co-za-menu.pdf", PageCapture.PdfName(page));
        Assert.Equal("karoocoffee-co-za-menu-phone-390.png", PageCapture.ShotName(page, Phone));
    }

    [Fact]
    public void A_name_already_used_gets_a_number()
    {
        var there = new HashSet<string> { "a.png", "a (2).png" };
        Assert.Equal("b.png", PageCapture.Free("b.png", there.Contains));
        Assert.Equal("a (3).png", PageCapture.Free("a.png", there.Contains));
    }

    [Fact]
    public void Says_what_was_saved()
    {
        Assert.Equal("1 picture saved.", PageCapture.Saved(1, 0));
        Assert.Equal("4 pictures saved.", PageCapture.Saved(4, 0));
        Assert.Equal("1 picture saved. The page is very long, so it stops at 16000 pixels down.", PageCapture.Saved(1, 1));
        Assert.Equal("4 pictures saved. The page is very long, so they stop at 16000 pixels down.", PageCapture.Saved(4, 4));
        Assert.Equal("4 pictures saved. The page is very long, so some stop at 16000 pixels down.", PageCapture.Saved(4, 1));
    }

    [Fact]
    public void Says_why_a_page_didnt_open_in_plain_words()
    {
        Assert.Equal("The site answered 404 (not found).", PageCapture.Problem(404, ""));
        Assert.Equal("There is no site at that address. Check the spelling.", PageCapture.Problem(0, "HostNameNotResolved"));
        Assert.Equal("The site's security certificate isn't right, so it wasn't opened.", PageCapture.Problem(0, "CertificateExpired"));
        Assert.Equal("The page didn't open (SomethingNew).", PageCapture.Problem(0, "SomethingNew"));
        Assert.Equal("The page didn't open.", PageCapture.Problem(0, ""));
    }

    [Fact]
    public void Paper_sizes_are_in_inches_upright()
    {
        Assert.Equal((8.27, 11.69), new PdfChoices().Inches);
        Assert.Equal((11.69, 16.54), new PdfChoices(PaperSize.A3, Sideways: true).Inches);
        Assert.Equal((8.5, 11), new PdfChoices(PaperSize.Letter).Inches);
    }

    [Fact]
    public void The_messages_to_the_browser_are_what_its_developer_tools_expect()
    {
        using var screen = JsonDocument.Parse(PageCapture.ScreenMessage(390, 844));
        Assert.Equal(390, screen.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(844, screen.RootElement.GetProperty("height").GetInt32());
        Assert.Equal(1, screen.RootElement.GetProperty("deviceScaleFactor").GetInt32());
        Assert.False(screen.RootElement.GetProperty("mobile").GetBoolean());

        using var picture = JsonDocument.Parse(PageCapture.PictureMessage(390, 3142));
        Assert.Equal("png", picture.RootElement.GetProperty("format").GetString());
        Assert.True(picture.RootElement.GetProperty("captureBeyondViewport").GetBoolean());
        Assert.Equal(3142, picture.RootElement.GetProperty("clip").GetProperty("height").GetInt32());

        using var scroll = JsonDocument.Parse(PageCapture.ScrollMessage);
        Assert.True(scroll.RootElement.GetProperty("awaitPromise").GetBoolean());
    }

    [Fact]
    public void Reads_the_browsers_answers_and_shrugs_at_nonsense()
    {
        Assert.Equal(3141.5, PageCapture.PageHeight("""{"result":{"type":"number","value":3141.5,"description":"3141.5"}}"""));
        Assert.Null(PageCapture.PageHeight("""{"result":{"type":"undefined"}}"""));
        Assert.Null(PageCapture.PageHeight("not json"));
        Assert.Equal([1, 2, 3], PageCapture.Picture("""{"data":"AQID"}"""));
        Assert.Null(PageCapture.Picture("""{"data":"***"}"""));
        Assert.Null(PageCapture.Picture("{}"));
        Assert.Null(PageCapture.Picture(null));
    }

    [Fact]
    public async Task Taking_a_picture_sets_the_screen_scrolls_shoots_and_puts_the_browser_back()
    {
        var calls = new List<string>();
        Task<string> Browser(string method, string message)
        {
            calls.Add(method);
            return Task.FromResult(method switch
            {
                "Runtime.evaluate" => """{"result":{"type":"number","value":2000}}""",
                "Page.captureScreenshot" => """{"data":"AQID"}""",
                _ => "{}",
            });
        }

        var shot = await PageCapture.ShootAsync(Browser, Phone, wholePage: true);

        Assert.Equal(["Emulation.setDeviceMetricsOverride", "Runtime.evaluate", "Page.captureScreenshot", "Emulation.clearDeviceMetricsOverride"], calls);
        Assert.Equal((390, 2000, false), (shot.Width, shot.Height, shot.Cut));
        Assert.Equal([1, 2, 3], shot.Png);
    }

    [Fact]
    public async Task The_browser_is_put_back_even_when_no_picture_comes()
    {
        var calls = new List<string>();
        Task<string> Browser(string method, string message)
        {
            calls.Add(method);
            return Task.FromResult("{}");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => PageCapture.ShootAsync(Browser, Phone, wholePage: false));
        Assert.Equal("Emulation.clearDeviceMetricsOverride", calls[^1]);
    }
}
