using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class ScreenshotTests
{
    [Theory]
    [InlineData("Ctrl+Alt+S", "Ctrl+Alt+S")]
    [InlineData("ctrl + shift + 2", "Ctrl+Shift+2")]
    [InlineData("PrtSc", "PrintScreen")]
    [InlineData("F9", "F9")]
    [InlineData("Win+Shift+f12", "Shift+Win+F12")]
    public void Shortcuts_are_read_the_way_people_write_them(string typed, string shown)
    {
        var (hotkey, problem) = Hotkey.Parse(typed);
        Assert.Null(problem);
        Assert.Equal(shown, hotkey!.ToString());
    }

    [Theory]
    [InlineData("S")]
    [InlineData("Shift+S")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Hyper+S")]
    [InlineData("")]
    public void Shortcuts_that_would_fire_while_typing_or_cant_be_had_are_refused(string typed)
    {
        var (hotkey, problem) = Hotkey.Parse(typed);
        Assert.Null(hotkey);
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_drag_becomes_a_region_inside_the_screen()
    {
        var screen = new ScreenGrab.Area(0, 0, 1920, 1080);
        Assert.Equal(new ScreenGrab.Area(100, 50, 200, 150), ScreenGrab.Region(300, 200, 100, 50, screen));
        Assert.Equal(new ScreenGrab.Area(1800, 1000, 120, 80), ScreenGrab.Region(1800, 1000, 2500, 1300, screen));
        Assert.True(ScreenGrab.IsClick(10, 10, 13, 12));
        Assert.False(ScreenGrab.IsClick(10, 10, 30, 12));
    }

    [Fact]
    public void A_click_takes_the_front_window_under_it_cut_to_the_screen()
    {
        var screen = new ScreenGrab.Area(1920, 0, 1920, 1080);
        ScreenGrab.Area[] windows = [new(2000, 100, 400, 300), new(1800, 0, 1000, 800)];
        Assert.Equal(windows[0], ScreenGrab.WindowAt(2100, 200, windows, screen));
        Assert.Equal(new ScreenGrab.Area(1920, 0, 880, 800), ScreenGrab.WindowAt(1950, 700, windows, screen));
        Assert.Equal(screen, ScreenGrab.WindowAt(3500, 1000, windows, screen));
    }

    [Fact]
    public void Text_marks_need_words_and_blur_needs_a_drag()
    {
        var at = new MarkPoint(10, 10);
        Assert.Empty(Markup.Add([], new Mark(MarkKind.Text, MarkColour.Red, [at]) { Comment = "  " }));
        Assert.Equal("Click here", Markup.Add([], new Mark(MarkKind.Text, MarkColour.Red, [at, new(50, 50)]) { Comment = " Click here " }).Single().Comment);
        Assert.Empty(Markup.Add([], new Mark(MarkKind.Blur, MarkColour.Red, [at, new(12, 12)])));
        Assert.Single(Markup.Add([], new Mark(MarkKind.Blur, MarkColour.Red, [at, new(80, 40)])));
    }

    [Fact]
    public void A_blur_turns_each_square_into_the_average_of_what_was_under_it()
    {
        // 4 x 2 picture: left half black, right half white.
        const int width = 4, height = 2, stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
            for (var x = 2; x < width; x++)
                pixels.AsSpan(y * stride + x * 4, 4).Fill(255);
        var squares = Markup.Pixelate(pixels, stride, width, height, new(0, 0), new(4, 2), block: 2);
        Assert.Equal(2, squares.Count);
        Assert.Equal((0, 0, 2, 2, (byte)0, (byte)0, (byte)0), squares[0]);
        Assert.Equal((2, 0, 2, 2, (byte)255, (byte)255, (byte)255), squares[1]);
        var mixed = Markup.Pixelate(pixels, stride, width, height, new(1, 0), new(3, 1), block: 2).Single();
        Assert.Equal((byte)127, mixed.R);
    }
}
