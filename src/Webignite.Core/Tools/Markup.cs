using System.Globalization;
using System.Text;

namespace Webignite.Core.Tools;

public enum MarkKind { Pin, Arrow, Box, Highlight, Pen, Text, Blur }

public enum MarkColour { Red, Yellow, Blue }

/// <summary>A point on the screenshot, in the image's own pixels so marks stay put at any zoom.</summary>
public readonly record struct MarkPoint(double X, double Y);

/// <summary>
/// One mark drawn on a screenshot. Pins are numbered and carry the comment that goes in the mail; a text mark
/// carries its words in <see cref="Comment"/>; a blur hides what is under it for good in the saved picture.
/// </summary>
public sealed record Mark(MarkKind Kind, MarkColour Colour, IReadOnlyList<MarkPoint> Points)
{
    public int Number { get; init; }
    public string Comment { get; init; } = "";
}

/// <summary>Rules for marking up a client's site: numbering pins, tidying shapes and writing the feedback.</summary>
public static class Markup
{
    /// <summary>Drags shorter than this are taken as a slip, not a mark.</summary>
    public const double MinDrag = 6;

    /// <summary>Adds a mark. A pin gets the next number; a drag too small to see is ignored.</summary>
    public static IReadOnlyList<Mark> Add(IReadOnlyList<Mark> marks, Mark mark)
    {
        if (mark.Points.Count == 0)
            return marks;
        if (mark.Kind == MarkKind.Pin)
            return [.. marks, mark with { Points = [mark.Points[0]], Number = NextNumber(marks) }];
        if (mark.Kind == MarkKind.Text)
            return string.IsNullOrWhiteSpace(mark.Comment) ? marks : [.. marks, mark with { Points = [mark.Points[0]], Comment = mark.Comment.Trim() }];
        if (Extent(mark.Points) < MinDrag)
            return marks;
        return [.. marks, mark];
    }

    public static int NextNumber(IEnumerable<Mark> marks) => marks.Where(m => m.Kind == MarkKind.Pin).Select(m => m.Number).DefaultIfEmpty(0).Max() + 1;

    /// <summary>Takes the last mark away. Pins after it keep their numbers, so the comments still match.</summary>
    public static IReadOnlyList<Mark> Undo(IReadOnlyList<Mark> marks) => marks.Count == 0 ? marks : marks.Take(marks.Count - 1).ToList();

    /// <summary>Removes one pin and numbers the rest 1, 2, 3 again in the order they were placed.</summary>
    public static IReadOnlyList<Mark> RemovePin(IReadOnlyList<Mark> marks, int number)
    {
        var next = 1;
        return marks.Where(m => !(m.Kind == MarkKind.Pin && m.Number == number))
            .Select(m => m.Kind == MarkKind.Pin ? m with { Number = next++ } : m)
            .ToList();
    }

    public static IReadOnlyList<Mark> SetComment(IReadOnlyList<Mark> marks, int number, string comment) =>
        marks.Select(m => m.Kind == MarkKind.Pin && m.Number == number ? m with { Comment = comment } : m).ToList();

    /// <summary>The box a drag covers, from any corner to any corner.</summary>
    public static (double X, double Y, double Width, double Height) Box(MarkPoint a, MarkPoint b) =>
        (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>The two short lines of an arrow head at the end point, angled back along the shaft.</summary>
    public static (MarkPoint Left, MarkPoint Right) ArrowHead(MarkPoint from, MarkPoint to, double length)
    {
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double spread = Math.PI / 7;
        return (new MarkPoint(to.X - length * Math.Cos(angle - spread), to.Y - length * Math.Sin(angle - spread)),
                new MarkPoint(to.X - length * Math.Cos(angle + spread), to.Y - length * Math.Sin(angle + spread)));
    }

    /// <summary>Marks scale with the picture so they read the same on a phone screenshot or a 4K one.</summary>
    public static double StrokeFor(int imageWidth, int imageHeight) => Math.Clamp(Math.Max(imageWidth, imageHeight) / 400d, 2, 8);

    /// <summary>Text size that reads well on the picture, like the strokes.</summary>
    public static double TextSize(int imageWidth, int imageHeight) => StrokeFor(imageWidth, imageHeight) * 7;

    /// <summary>How big the squares of a blur are: big enough that nothing under them can be read back.</summary>
    public static int BlurBlock(int imageWidth, int imageHeight) => (int)Math.Clamp(Math.Max(imageWidth, imageHeight) / 90d, 8, 28);

    /// <summary>
    /// A blur as squares of one colour each, the average of the pixels under it, so passwords and client details
    /// can't be read back from the saved picture. The picture is BGRA, four bytes a pixel.
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height, byte B, byte G, byte R)> Pixelate(
        ReadOnlySpan<byte> bgra, int stride, int width, int height, MarkPoint a, MarkPoint b, int block)
    {
        var (bx, by, bw, bh) = Box(a, b);
        var left = (int)Math.Clamp(Math.Floor(bx), 0, width);
        var top = (int)Math.Clamp(Math.Floor(by), 0, height);
        var right = (int)Math.Clamp(Math.Ceiling(bx + bw), 0, width);
        var bottom = (int)Math.Clamp(Math.Ceiling(by + bh), 0, height);
        var squares = new List<(int, int, int, int, byte, byte, byte)>();
        for (var y = top; y < bottom; y += block)
        {
            for (var x = left; x < right; x += block)
            {
                var w = Math.Min(block, right - x);
                var h = Math.Min(block, bottom - y);
                long sb = 0, sg = 0, sr = 0;
                for (var row = y; row < y + h; row++)
                {
                    for (var column = x; column < x + w; column++)
                    {
                        var at = row * stride + column * 4;
                        sb += bgra[at];
                        sg += bgra[at + 1];
                        sr += bgra[at + 2];
                    }
                }
                var count = w * h;
                squares.Add((x, y, w, h, (byte)(sb / count), (byte)(sg / count), (byte)(sr / count)));
            }
        }
        return squares;
    }

    private static double Extent(IReadOnlyList<MarkPoint> points)
    {
        var w = points.Max(p => p.X) - points.Min(p => p.X);
        var h = points.Max(p => p.Y) - points.Min(p => p.Y);
        return Math.Max(w, h);
    }

    /// <summary>The numbered comments as a list for the mail, skipping pins with nothing written.</summary>
    public static string Feedback(IEnumerable<Mark> marks)
    {
        var text = new StringBuilder();
        foreach (var pin in marks.Where(m => m.Kind == MarkKind.Pin && !string.IsNullOrWhiteSpace(m.Comment)).OrderBy(m => m.Number))
            text.Append(CultureInfo.InvariantCulture, $"{pin.Number}. {pin.Comment.Trim()}\n");
        return text.ToString().TrimEnd();
    }

    /// <summary>The mail that goes with a marked-up screenshot.</summary>
    public static (string Subject, string Body) Mail(string? clientName, string? contactPerson, string? page, IEnumerable<Mark> marks, string tradingName)
    {
        var first = contactPerson is { Length: > 0 } person ? person.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : clientName ?? "there";
        var about = string.IsNullOrWhiteSpace(page) ? "the website" : page.Trim();
        var feedback = Feedback(marks);
        var body = new StringBuilder($"Hi {first},\n\n");
        body.Append(feedback.Length > 0
            ? $"I've marked up {about} in the attached screenshot. The numbers match these notes:\n\n{feedback}\n\n"
            : $"I've marked up {about} in the attached screenshot.\n\n");
        body.Append("Let me know what you think.\n\nThanks,\n").Append(tradingName);
        return ($"Notes on {about}", body.ToString());
    }

    /// <summary>"Screenshot 2026-11-01 0930.png", or with the client's name in front.</summary>
    public static string FileName(string? clientName, DateTime now) =>
        (string.IsNullOrWhiteSpace(clientName) ? "" : ImageRules.Slug(clientName) + " ")
        + "screenshot " + now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ".png";
}
