using SkiaSharp;
using Webignite.Core.Documents;

namespace Webignite.Tools;

/// <summary>
/// Draws a quote or invoice as an A4 PDF from a <see cref="DocumentSheet"/>: real text throughout, so it prints sharp
/// and can be searched, with the Pay now button a link. Long documents run onto more pages.
/// </summary>
public static class DocumentPdfMaker
{
    // A4 in points, 72 to the inch.
    private const float PageWidth = 595, PageHeight = 842, Margin = 48, Bottom = PageHeight - 64;
    private const float AmountRight = PageWidth - Margin, PriceRight = AmountRight - 92, QuantityRight = PriceRight - 86, DescriptionWidth = QuantityRight - 50 - Margin;

    private static readonly SKColor Ink = new(28, 29, 33);
    private static readonly SKColor Muted = new(110, 114, 122);
    private static readonly SKColor Rule = new(222, 225, 229);
    private static readonly SKColor Soft = new(245, 246, 248);

    public static byte[] Make(DocumentSheet sheet)
    {
        using var plain = SKTypeface.FromFamilyName(null) ?? SKTypeface.Default;
        using var bold = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
        using var memory = new MemoryStream();
        using (var document = SKDocument.CreatePdf(memory, new SKDocumentPdfMetadata { Title = sheet.Title[..1] + sheet.Title[1..].ToLowerInvariant() + " " + sheet.Number, Creation = DateTime.Now }))
        {
            var page = new Page(document, sheet, plain, bold);
            page.Head();
            page.Lines();
            page.Totals();
            page.Words();
            page.Close();
            document.Close();
        }
        return memory.ToArray();
    }

    // The page being drawn and how far down it is.
    private sealed class Page(SKDocument document, DocumentSheet sheet, SKTypeface plain, SKTypeface bold)
    {
        private SKCanvas canvas = document.BeginPage(PageWidth, PageHeight);
        private float y = Margin;
        private int number = 1;

        public void Head()
        {
            var top = y;
            // The logo (or the business's name) on the left, what it is and its number on the right.
            var logoBottom = Logo(top);
            Right(sheet.Title, bold, 22, Ink, top + 20);
            Right(sheet.IsDraft ? "Draft" : sheet.Number, plain, 11, Muted, top + 38);
            y = Math.Max(logoBottom, top + 48) + 22;

            var fromTop = y;
            Label("FROM", Margin, y);
            var left = Block(sheet.From, Margin, y + 14, 215, firstBold: true);
            Label("TO", Margin + 232, fromTop);
            var middle = Block(sheet.To, Margin + 232, fromTop + 14, 140, firstBold: true);
            var factY = fromTop;
            foreach (var fact in sheet.Facts)
            {
                Label(fact.Label.ToUpperInvariant(), AmountRight - 116, factY);
                Text(Fit(fact.Value, plain, 9.5f, 116), plain, 9.5f, Ink, AmountRight - 116, factY + 23);
                factY += 36;
            }
            y = Math.Max(Math.Max(left, middle), factY) + 18;
        }

        public void Lines()
        {
            TableHead();
            foreach (var row in sheet.Rows)
            {
                var words = row.Description.Split('\n').SelectMany(l => Wrap(l, plain, 9.5f, row.Amount is null ? AmountRight - Margin : DescriptionWidth)).ToList();
                var height = Math.Max(1, words.Count) * 13 + 12;
                if (y + height > Bottom)
                {
                    Turn();
                    TableHead();
                }
                var baseline = y + 16;
                foreach (var line in words)
                {
                    Text(line, plain, 9.5f, Ink, Margin, baseline);
                    baseline += 13;
                }
                if (row.Amount is not null)
                {
                    RightAt(row.Quantity ?? "", plain, 9.5f, Ink, QuantityRight, y + 16);
                    RightAt(row.Price ?? "", plain, 9.5f, Ink, PriceRight, y + 16);
                    RightAt(row.Amount, plain, 9.5f, Ink, AmountRight, y + 16);
                }
                y += height;
                Line(y, Rule);
            }
        }

        public void Totals()
        {
            Need(sheet.Totals.Count * 20 + 24);
            y += 12;
            foreach (var total in sheet.Totals)
            {
                if (total.Strong)
                {
                    using var fill = new SKPaint { Color = Soft, IsAntialias = true };
                    canvas.DrawRoundRect(SKRect.Create(PriceRight - 96, y - 2, AmountRight - PriceRight + 96 + 8, 24), 4, 4, fill);
                }
                Text(total.Label, total.Strong ? bold : plain, total.Strong ? 11 : 9.5f, total.Strong ? Ink : Muted, PriceRight - 86, y + 14);
                RightAt(total.Value, total.Strong ? bold : plain, total.Strong ? 11 : 9.5f, Ink, AmountRight, y + 14);
                y += total.Strong ? 28 : 20;
            }
        }

        public void Words()
        {
            y += 10;
            if (sheet.Bank.Count > 0 || sheet.PayLink is not null)
            {
                Need(sheet.Bank.Count * 13 + 60);
                Label("HOW TO PAY", Margin, y);
                var top = y + 14;
                var bottom = Block(sheet.Bank, Margin, top, 300, firstBold: false);
                if (sheet.PayLink is { } link)
                {
                    var button = SKRect.Create(AmountRight - 150, top - 4, 150, 30);
                    using var fill = new SKPaint { Color = Ink, IsAntialias = true };
                    canvas.DrawRoundRect(button, 5, 5, fill);
                    using var font = new SKFont(bold, 10.5f) { Subpixel = true };
                    using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
                    const string words = "Pay now with PayFast";
                    canvas.DrawText(words, button.MidX - font.MeasureText(words) / 2, button.MidY + 4, font, white);
                    canvas.DrawUrlAnnotation(button, link);
                    bottom = Math.Max(bottom, button.Bottom + 8);
                }
                y = bottom + 14;
            }
            Paragraph("NOTES", sheet.Notes);
            Paragraph("TERMS", sheet.Terms);
        }

        public void Close()
        {
            Foot();
            document.EndPage();
        }

        private void Paragraph(string label, string? words)
        {
            if (string.IsNullOrWhiteSpace(words))
                return;
            var lines = words.Split('\n').SelectMany(l => l.Trim().Length == 0 ? [""] : Wrap(l.Trim(), plain, 9, PageWidth - 2 * Margin)).ToList();
            Need(Math.Min(lines.Count, 4) * 12.5f + 20);
            Label(label, Margin, y);
            y += 14;
            foreach (var line in lines)
            {
                if (y + 12.5f > Bottom)
                    Turn();
                Text(line, plain, 9, Ink, Margin, y + 9);
                y += 12.5f;
            }
            y += 12;
        }

        private float Logo(float top)
        {
            if (sheet.Logo is { Length: > 0 } logo)
            {
                try
                {
                    using var picture = SKBitmap.Decode(Convert.FromBase64String(logo));
                    if (picture is { Width: > 0, Height: > 0 })
                    {
                        var scale = Math.Min(190f / picture.Width, 56f / picture.Height);
                        var box = SKRect.Create(Margin, top, picture.Width * scale, picture.Height * scale);
                        using var image = SKImage.FromBitmap(picture);
                        canvas.DrawImage(image, box, new SKSamplingOptions(SKCubicResampler.Mitchell));
                        return box.Bottom;
                    }
                }
                catch (FormatException)
                {
                    // Not a picture after all: the name is shown instead.
                }
            }
            if (sheet.From.Count > 0)
                Text(Fit(sheet.From[0], bold, 18, 300), bold, 18, Ink, Margin, top + 20);
            return top + 26;
        }

        private void TableHead()
        {
            using var fill = new SKPaint { Color = Soft, IsAntialias = true };
            canvas.DrawRoundRect(SKRect.Create(Margin - 8, y, PageWidth - 2 * Margin + 16, 22), 4, 4, fill);
            Label("DESCRIPTION", Margin, y + 7);
            RightAt("QTY", plain, 8, Muted, QuantityRight, y + 15);
            RightAt("PRICE", plain, 8, Muted, PriceRight, y + 15);
            RightAt("AMOUNT", plain, 8, Muted, AmountRight, y + 15);
            y += 24;
        }

        // Lines of text one under the other, each cut to the width; gives where they end.
        private float Block(IReadOnlyList<string> lines, float x, float top, float width, bool firstBold)
        {
            var baseline = top + 9;
            for (var i = 0; i < lines.Count; i++)
            {
                var face = firstBold && i == 0 ? bold : plain;
                foreach (var part in Wrap(lines[i], face, 9.5f, width))
                {
                    Text(part, face, 9.5f, Ink, x, baseline);
                    baseline += 13;
                }
            }
            return baseline - 9;
        }

        private void Need(float height)
        {
            if (y + height > Bottom)
                Turn();
        }

        private void Turn()
        {
            Foot();
            document.EndPage();
            canvas = document.BeginPage(PageWidth, PageHeight);
            number++;
            y = Margin;
            Text($"{sheet.Title[..1]}{sheet.Title[1..].ToLowerInvariant()} {sheet.Number}, page {number}", plain, 8.5f, Muted, Margin, y + 8);
            y += 24;
        }

        private void Foot()
        {
            if (sheet.Footer is { } footer)
            {
                Line(PageHeight - 46, Rule);
                Text(Fit(footer, plain, 8.5f, PageWidth - 2 * Margin), plain, 8.5f, Muted, Margin, PageHeight - 30);
            }
        }

        private void Label(string words, float x, float top) => Text(words, plain, 7.5f, Muted, x, top + 8);

        private void Right(string words, SKTypeface face, float size, SKColor colour, float baseline) => RightAt(words, face, size, colour, AmountRight, baseline);

        private void RightAt(string words, SKTypeface face, float size, SKColor colour, float right, float baseline)
        {
            using var font = new SKFont(face, size) { Subpixel = true };
            Text(words, face, size, colour, right - font.MeasureText(words), baseline);
        }

        private void Line(float at, SKColor colour)
        {
            using var line = new SKPaint { Color = colour, StrokeWidth = 0.75f };
            canvas.DrawLine(Margin - 8, at, PageWidth - Margin + 8, at, line);
        }

        private void Text(string words, SKTypeface face, float size, SKColor colour, float x, float baseline)
        {
            using var font = new SKFont(face, size) { Subpixel = true };
            using var paint = new SKPaint { Color = colour, IsAntialias = true };
            canvas.DrawText(words, x, baseline, font, paint);
        }
    }

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
