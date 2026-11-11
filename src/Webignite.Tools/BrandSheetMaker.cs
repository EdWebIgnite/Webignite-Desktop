using SkiaSharp;
using Webignite.Core.Tools;

namespace Webignite.Tools;

/// <summary>
/// Draws a brand sheet as a one-page A4 PDF: the name, the logo on white and (when it has no background of its
/// own) in white on dark, the colours with their codes, and the two typefaces in use. Everything is drawn as
/// shapes and real text, so it prints sharp and the codes can be copied out of it.
/// </summary>
public static class BrandSheetMaker
{
    // A4 in points, 72 to the inch.
    private const float PageWidth = 595, PageHeight = 842, Margin = 48;

    private static readonly SKColor Ink = new(28, 29, 33);
    private static readonly SKColor Muted = new(110, 114, 122);
    private static readonly SKColor Line = new(222, 225, 229);

    /// <summary>Writes the sheet. Gives the typefaces asked for that this PC doesn't have, whose samples were drawn in a stand-in.</summary>
    public static IReadOnlyList<string> Write(BrandSheetContent content, string pdfPath)
    {
        var missing = new List<string>();
        using var plain = SKTypeface.FromFamilyName(null) ?? SKTypeface.Default;
        using var heading = Face(content.HeadingFont, SKFontStyle.Bold, missing) ?? SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
        using var body = Face(content.BodyFont, SKFontStyle.Normal, missing) ?? SKTypeface.FromFamilyName(null) ?? SKTypeface.Default;

        using var file = File.Create(pdfPath);
        using (var document = SKDocument.CreatePdf(file, new SKDocumentPdfMetadata { Title = content.Name.Trim() + " brand sheet", Creation = DateTime.Now }))
        {
            var canvas = document.BeginPage(PageWidth, PageHeight);
            var y = Margin;

            Text(canvas, "BRAND SHEET", plain, 9, Muted, Margin, y + 9);
            Text(canvas, content.Name.Trim(), heading, 28, Ink, Margin, y + 44);
            y += 62;
            Rule(canvas, y);
            y += 22;

            if (content.LogoPath.Length > 0)
                y = Logo(canvas, content.LogoPath, plain, y);
            var colours = BrandSheet.Shown(content.Colours);
            if (colours.Count > 0)
                y = Swatches(canvas, colours, plain, y);
            if (content.HeadingFont.Trim().Length > 0 || content.BodyFont.Trim().Length > 0)
                Type(canvas, content, heading, body, plain, y);

            if (content.PreparedBy.Trim().Length > 0)
            {
                Rule(canvas, PageHeight - Margin - 16);
                Text(canvas, "Prepared by " + content.PreparedBy.Trim(), plain, 8.5f, Muted, Margin, PageHeight - Margin);
            }
            document.EndPage();
            document.Close();
        }
        return missing;
    }

    // The logo on white, and beside it in white on dark when it has no background of its own.
    private static float Logo(SKCanvas canvas, string path, SKTypeface plain, float y)
    {
        using var logo = LogoMaster.Open(path);
        Label(canvas, "LOGO", plain, y);
        y += 12;
        const float height = 150;
        var width = logo.SeeThrough ? (PageWidth - 2 * Margin - 14) / 2 : PageWidth - 2 * Margin;
        var light = SKRect.Create(Margin, y, width, height);
        using (var edge = new SKPaint { Color = Line, IsStroke = true, StrokeWidth = 0.75f, IsAntialias = true })
            canvas.DrawRoundRect(light, 6, 6, edge);
        logo.DrawOn(canvas, Inset(light, 22), LogoInk.Colour);
        if (logo.SeeThrough)
        {
            var dark = SKRect.Create(Margin + width + 14, y, width, height);
            using (var fill = new SKPaint { Color = Ink, IsAntialias = true })
                canvas.DrawRoundRect(dark, 6, 6, fill);
            logo.DrawOn(canvas, Inset(dark, 22), LogoInk.White);
        }
        return y + height + 26;
    }

    // Up to four colours a row: a block of the colour with its name on it, and its codes beneath.
    private static float Swatches(SKCanvas canvas, IReadOnlyList<(Swatch Swatch, Rgb Colour)> colours, SKTypeface plain, float y)
    {
        Label(canvas, "COLOURS", plain, y);
        y += 12;
        const int across = 4;
        const float gap = 12, block = 62;
        var width = (PageWidth - 2 * Margin - gap * (across - 1)) / across;
        for (var i = 0; i < colours.Count; i++)
        {
            var (swatch, colour) = colours[i];
            var x = Margin + i % across * (width + gap);
            var top = y + i / across * (block + 54);
            var box = SKRect.Create(x, top, width, block);
            using (var fill = new SKPaint { Color = new SKColor(colour.R, colour.G, colour.B), IsAntialias = true })
                canvas.DrawRoundRect(box, 5, 5, fill);
            // A colour close to white needs an edge to be seen on the page.
            if (Colours.Contrast(colour, new Rgb(255, 255, 255)) < 1.25)
            {
                using var edge = new SKPaint { Color = Line, IsStroke = true, StrokeWidth = 0.75f, IsAntialias = true };
                canvas.DrawRoundRect(box, 5, 5, edge);
            }
            Text(canvas, Fit(swatch.Name, plain, 9.5f, width - 16), plain, 9.5f, BrandSheet.WhiteOn(colour) ? SKColors.White : Ink, x + 8, top + block - 9, bold: true);
            var line = top + block + 13;
            foreach (var code in BrandSheet.Codes(colour))
            {
                Text(canvas, code, plain, 8, code[0] == '#' ? Ink : Muted, x, line);
                line += 11;
            }
        }
        var rows = (colours.Count + across - 1) / across;
        return y + rows * (block + 54) + 14;
    }

    private static void Type(SKCanvas canvas, BrandSheetContent content, SKTypeface heading, SKTypeface body, SKTypeface plain, float y)
    {
        Label(canvas, "TYPEFACES", plain, y);
        y += 16;
        var width = PageWidth - 2 * Margin;
        if (content.HeadingFont.Trim().Length > 0)
        {
            Text(canvas, "Headings: " + content.HeadingFont.Trim(), plain, 8.5f, Muted, Margin, y + 8);
            Text(canvas, Fit(BrandSheet.HeadingSample, heading, 24, width), heading, 24, Ink, Margin, y + 38);
            y += 56;
        }
        if (content.BodyFont.Trim().Length > 0)
        {
            Text(canvas, "Text: " + content.BodyFont.Trim(), plain, 8.5f, Muted, Margin, y + 8);
            var line = y + 28;
            foreach (var words in Wrap(BrandSheet.BodySample, body, 12, width))
            {
                Text(canvas, words, body, 12, Ink, Margin, line);
                line += 17;
            }
        }
    }

    // The typeface by name, or null (and a note of it) when the PC gives back a different one.
    private static SKTypeface? Face(string name, SKFontStyle style, List<string> missing)
    {
        var wanted = name.Trim();
        if (wanted.Length == 0)
            return null;
        var found = SKTypeface.FromFamilyName(wanted, style);
        if (found is not null && string.Equals(found.FamilyName, wanted, StringComparison.OrdinalIgnoreCase))
            return found;
        found?.Dispose();
        missing.Add(wanted);
        return null;
    }

    private static void Label(SKCanvas canvas, string words, SKTypeface plain, float y) => Text(canvas, words, plain, 8, Muted, Margin, y);

    private static void Rule(SKCanvas canvas, float y)
    {
        using var line = new SKPaint { Color = Line, StrokeWidth = 0.75f };
        canvas.DrawLine(Margin, y, PageWidth - Margin, y, line);
    }

    private static SKRect Inset(SKRect box, float by) => new(box.Left + by, box.Top + by, box.Right - by, box.Bottom - by);

    private static void Text(SKCanvas canvas, string words, SKTypeface face, float size, SKColor colour, float x, float baseline, bool bold = false)
    {
        using var font = new SKFont(face, size) { Embolden = bold, Subpixel = true };
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        canvas.DrawText(words, x, baseline, font, paint);
    }

    // The words cut short with an ellipsis when they are wider than the room.
    private static string Fit(string words, SKTypeface face, float size, float width)
    {
        using var font = new SKFont(face, size);
        if (font.MeasureText(words) <= width)
            return words;
        var cut = words;
        while (cut.Length > 1 && font.MeasureText(cut + "…") > width)
            cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }

    private static List<string> Wrap(string words, SKTypeface face, float size, float width)
    {
        using var font = new SKFont(face, size);
        var lines = new List<string>();
        var line = "";
        foreach (var word in words.Split(' '))
        {
            var longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureText(longer) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = longer;
            }
        }
        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }
}
