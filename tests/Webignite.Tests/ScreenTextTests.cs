using SkiaSharp;
using Webignite.Core.Tools;
using Webignite.Tools;

namespace Webignite.Tests;

public class ScreenTextTests
{
    [Fact]
    public void Lines_are_trimmed_and_empty_ones_dropped() =>
        Assert.Equal("Karoo Coffee" + Environment.NewLine + "Small-batch coffee", ScreenText.Tidy(["  Karoo Coffee ", "", "   ", "Small-batch coffee"]));

    [Fact]
    public void The_note_counts_the_words_and_shows_how_it_starts()
    {
        Assert.Equal("1 word copied: Hello", ScreenText.Summary("Hello"));
        Assert.Equal("3 words copied: Small-batch coffee roasters", ScreenText.Summary("Small-batch coffee\r\nroasters"));
        Assert.Equal("10 words copied: one two three four five six seven eight…", ScreenText.Summary("one two three four five six seven eight nine ten"));
    }

    [Fact]
    public void Small_pictures_are_enlarged_before_reading_and_big_ones_left()
    {
        Assert.Equal(2, ScreenText.Enlarge(600, 200, 10000));
        Assert.Equal(1, ScreenText.Enlarge(3000, 2000, 10000));
        Assert.Equal(1, ScreenText.Enlarge(6000, 200, 10000));
        Assert.Equal(1, ScreenText.Enlarge(0, 0, 10000));
    }

    /// <summary>
    /// Windows' own text reading, on Windows only, and only where it has a reader for the PC's language (a
    /// desktop PC does; a server may not). Words drawn in a picture must come back as text, a line per line.
    /// </summary>
    [Fact]
    public async Task Windows_reads_the_words_in_a_picture()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Assert.SkipUnless(WindowsOcr.Available(), "Windows has no text reader for this PC's language.");

        using var picture = new SKBitmap(new SKImageInfo(640, 200, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(picture))
        using (var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 34))
        using (var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("Karoo Coffee Roasters", 30, 70, font, ink);
            canvas.DrawText("Invoice 2026 is paid", 30, 140, font, ink);
        }

        var text = await WindowsOcr.ReadAsync(picture.Bytes, picture.Width, picture.Height);

        Assert.NotNull(text);
        var lines = text.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Karoo Coffee", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026", lines[1], StringComparison.Ordinal);

        // A blank picture has no words, and says so with nothing rather than failing.
        using var blank = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Bgra8888, SKAlphaType.Opaque));
        blank.Erase(SKColors.White);
        Assert.Equal("", await WindowsOcr.ReadAsync(blank.Bytes, blank.Width, blank.Height));
    }
}
