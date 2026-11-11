using System.Globalization;

namespace Webignite.Core.Tools;

/// <summary>A stretch of pixels along a line, both ends included.</summary>
public readonly record struct PixelRun(int Start, int End)
{
    public int Length => End - Start + 1;
}

/// <summary>What was measured on the screen, in real pixels.</summary>
public readonly record struct Measured(int Width, int Height);

/// <summary>
/// The screen ruler's rules. Pointing at something measures the stretch of one colour through the pointer, across
/// and down: the gap between two things, or the size of a flat block. Dragging measures a box.
/// </summary>
public static class ScreenRuler
{
    /// <summary>How far a colour may drift, per channel, and still count as the same stretch. Soaks up smoothing at edges of text.</summary>
    public const int Tolerance = 10;

    /// <summary>
    /// The stretch of pixels through position <paramref name="at"/> that are the colour found there, give or
    /// take the tolerance. Pixels are 32-bit with the top byte ignored.
    /// </summary>
    public static PixelRun Reach(ReadOnlySpan<uint> line, int at, int tolerance = Tolerance)
    {
        if (line.Length == 0)
            return new PixelRun(0, 0);
        at = Math.Clamp(at, 0, line.Length - 1);
        var colour = line[at];
        var start = at;
        while (start > 0 && Near(line[start - 1], colour, tolerance))
            start--;
        var end = at;
        while (end < line.Length - 1 && Near(line[end + 1], colour, tolerance))
            end++;
        return new PixelRun(start, end);
    }

    private static bool Near(uint a, uint b, int tolerance) =>
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) <= tolerance
        && Math.Abs((int)(a >> 8 & 0xFF) - (int)(b >> 8 & 0xFF)) <= tolerance
        && Math.Abs((int)(a >> 16 & 0xFF) - (int)(b >> 16 & 0xFF)) <= tolerance;

    /// <summary>The box between two corners, whichever way it was dragged. Both corner pixels are inside it.</summary>
    public static (int X, int Y, Measured Size) Box(int x1, int y1, int x2, int y2) =>
        (Math.Min(x1, x2), Math.Min(y1, y2), new Measured(Math.Abs(x2 - x1) + 1, Math.Abs(y2 - y1) + 1));

    /// <summary>A drag shorter than this, in pixels, is a click.</summary>
    public const int ClickSlack = 3;

    /// <summary>"480 × 270", as copied.</summary>
    public static string Plain(Measured size) =>
        $"{size.Width.ToString(CultureInfo.InvariantCulture)} × {size.Height.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// "480 × 270 px", and on a screen Windows enlarges, what that is at 100%: "(320 × 180 at 100%)", which is
    /// the size a website's CSS would call it.
    /// </summary>
    public static string Words(Measured size, double scaling)
    {
        var words = Plain(size) + " px";
        if (scaling is <= 0 or (> 0.995 and < 1.005))
            return words;
        var plain = new Measured((int)Math.Round(size.Width / scaling), (int)Math.Round(size.Height / scaling));
        return $"{words} ({Plain(plain)} at 100%)";
    }

    /// <summary>One length on its own, for the lines drawn while pointing.</summary>
    public static string Length(int pixels) => pixels.ToString(CultureInfo.InvariantCulture);
}
