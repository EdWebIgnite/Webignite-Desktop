using System.Text.Json;
using Webignite.Core.Web;

namespace Webignite.Tests;

public class DevicesTests
{
    private static Device Named(string name) => Devices.All.First(d => d.Name == name);

    [Fact]
    public void There_is_a_range_of_iphones_samsungs_ipads_and_computers()
    {
        Assert.True(Devices.All.Count(d => d.Name.StartsWith("iPhone", StringComparison.Ordinal)) >= 3);
        Assert.True(Devices.All.Count(d => d.Name.StartsWith("Samsung", StringComparison.Ordinal)) >= 4);
        Assert.True(Devices.All.Count(d => d.Name.StartsWith("iPad", StringComparison.Ordinal)) >= 3);
        Assert.Contains(Devices.All, d => d.Name.StartsWith("Google Pixel", StringComparison.Ordinal));
        Assert.Contains(Devices.All, d => d.Width == 360 && d.Name.StartsWith("Budget", StringComparison.Ordinal));
        Assert.Equal(["Laptop", "Desktop"], Devices.All.Where(d => d.Kind == DeviceKind.Computer).Select(d => d.Name));
        Assert.Equal(Devices.All.Count, Devices.All.Select(d => d.Name).Distinct().Count());
    }

    [Fact]
    public void Phones_and_tablets_are_upright_say_who_they_are_and_are_touched()
    {
        foreach (var device in Devices.All.Where(d => d.Mobile))
        {
            Assert.True(device.Height > device.Width, device.Name);
            Assert.True(device.Density >= 2, device.Name);
            Assert.Contains("Mozilla/5.0", device.UserAgent, StringComparison.Ordinal);
        }
        Assert.Contains("iPhone", Named("iPhone 16").UserAgent, StringComparison.Ordinal);
        Assert.Contains("iPad", Named("iPad Air").UserAgent, StringComparison.Ordinal);
        Assert.Contains("Android", Named("Samsung Galaxy S24").UserAgent, StringComparison.Ordinal);
        Assert.Contains(" Mobile ", Named("Samsung Galaxy S24").UserAgent, StringComparison.Ordinal);
        Assert.DoesNotContain(" Mobile ", Named("Samsung Galaxy Tab S9").UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_computer_keeps_the_browsers_own_name_and_a_mouse()
    {
        var laptop = Named("Laptop");
        Assert.False(laptop.Mobile);
        Assert.Equal("", laptop.UserAgent);
        Assert.Equal((1366, 768), laptop.Screen(sideways: false));
    }

    [Fact]
    public void Sideways_swaps_width_and_height() => Assert.Equal((844, 390), Named("iPhone 14").Screen(sideways: true));

    [Theory]
    [InlineData("600", "900", 600, 900, DeviceKind.Tablet)]
    [InlineData(" 320 ", "568", 320, 568, DeviceKind.Phone)]
    [InlineData("1440", "900", 1440, 900, DeviceKind.Computer)]
    public void A_typed_size_makes_a_device(string width, string height, int w, int h, DeviceKind kind)
    {
        var device = Devices.Custom(width, height);
        Assert.NotNull(device);
        Assert.Equal((w, h, kind), (device.Width, device.Height, device.Kind));
    }

    [Theory]
    [InlineData("", "900")]
    [InlineData("wide", "900")]
    [InlineData("100", "900")]
    [InlineData("600", "99999")]
    [InlineData("-600", "900")]
    public void A_size_that_makes_no_sense_makes_none(string width, string height) => Assert.Null(Devices.Custom(width, height));

    [Fact]
    public void A_screen_that_fits_is_shown_life_size_and_never_bigger()
    {
        Assert.Equal(1, Devices.Fit(390, 844, 1100, 900));
        Assert.Equal(1, Devices.Fit(390, 844, 5000, 5000));
    }

    [Fact]
    public void A_screen_too_big_for_the_room_is_shrunk_to_fit()
    {
        Assert.Equal(0.5, Devices.Fit(1024, 1366, 1100, 683));
        Assert.Equal(0.57, Devices.Fit(1920, 1080, 1100, 900));
        Assert.Equal(0.1, Devices.Fit(1920, 1080, 20, 20));
        Assert.Equal(1, Devices.Fit(390, 844, 0, 0));
    }

    [Fact]
    public void The_size_is_said_with_how_much_it_was_shrunk()
    {
        Assert.Equal("390 × 844", Devices.SizeWords(390, 844, 1));
        Assert.Equal("1024 × 1366, shown at 50%", Devices.SizeWords(1024, 1366, 0.5));
    }

    [Fact]
    public async Task Showing_as_a_phone_sets_its_screen_name_and_touch()
    {
        var calls = new List<(string Method, string Message)>();
        Task<string> Browser(string method, string message)
        {
            calls.Add((method, message));
            return Task.FromResult("{}");
        }

        await Devices.ApplyAsync(Browser, Named("iPhone 14"), sideways: true, fit: 0.8);

        Assert.Equal(["Emulation.setDeviceMetricsOverride", "Emulation.setUserAgentOverride", "Emulation.setTouchEmulationEnabled", "Emulation.setEmitTouchEventsForMouse"],
            calls.Select(c => c.Method));
        using var screen = JsonDocument.Parse(calls[0].Message);
        Assert.Equal(844, screen.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(390, screen.RootElement.GetProperty("height").GetInt32());
        Assert.Equal(3, screen.RootElement.GetProperty("deviceScaleFactor").GetDouble());
        Assert.True(screen.RootElement.GetProperty("mobile").GetBoolean());
        Assert.Equal(0.8, screen.RootElement.GetProperty("scale").GetDouble());
        Assert.Contains("iPhone", calls[1].Message, StringComparison.Ordinal);
        Assert.Contains("\"enabled\":true", calls[2].Message, StringComparison.Ordinal);
        Assert.Contains("\"configuration\":\"mobile\"", calls[3].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Showing_as_a_computer_leaves_the_name_and_mouse_alone()
    {
        var calls = new List<(string Method, string Message)>();
        Task<string> Browser(string method, string message)
        {
            calls.Add((method, message));
            return Task.FromResult("{}");
        }

        await Devices.ApplyAsync(Browser, Named("Desktop"), sideways: false, fit: 0.57);

        Assert.Contains("\"mobile\":false", calls[0].Message, StringComparison.Ordinal);
        Assert.Equal("""{"userAgent":""}""", calls[1].Message);
        Assert.Equal("""{"enabled":false}""", calls[2].Message);
        Assert.Equal("""{"enabled":false}""", calls[3].Message);
    }

    [Fact]
    public async Task Going_back_to_the_window_undoes_all_of_it()
    {
        var calls = new List<(string Method, string Message)>();
        Task<string> Browser(string method, string message)
        {
            calls.Add((method, message));
            return Task.FromResult("{}");
        }

        await Devices.ClearAsync(Browser);

        Assert.Equal("Emulation.clearDeviceMetricsOverride", calls[0].Method);
        Assert.Equal("""{"userAgent":""}""", calls[1].Message);
        Assert.All(calls.Skip(2), c => Assert.Equal("""{"enabled":false}""", c.Message));
    }

    [Fact]
    public void A_device_picture_is_named_after_the_device() =>
        Assert.Equal("karoocoffee-co-za-menu-iphone-16-pro-max-440.png",
            PageCapture.ShotName(new Uri("https://karoocoffee.co.za/menu"), new ShotWidth("iPhone 16 Pro Max", 440, 956)));
}
