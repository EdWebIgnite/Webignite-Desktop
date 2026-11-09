using SkiaSharp;
using Webignite.Core.Tools;
using Webignite.Tools;

namespace Webignite.Tests;

/// <summary>Putting PDFs together with PDFium: pages in a new order, turned, with words and a picture put on.</summary>
public sealed class PdfEditTests
{
    // A one-page PDF, 600 by 800 points, with a line of text.
    private static byte[] Sample(string words)
    {
        var text = $"BT /F1 24 Tf 60 700 Td ({words}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {text.Length} >>\nstream\n{text}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    // The colour of a point on a page, as shares of the page shown, drawn about 300 pixels wide.
    private static (byte R, byte G, byte B) ColourAt(byte[] pdf, int page, double x, double y)
    {
        var dotsPerInch = (int)(300 / PdfEdit.PageSizes(pdf)[page].Width * 72);
        using var drawn = SKBitmap.Decode(PdfEdit.Pictures(pdf, dotsPerInch)[page]);
        var colour = drawn.GetPixel((int)(x * drawn.Width), (int)(y * drawn.Height));
        return (colour.Red, colour.Green, colour.Blue);
    }

    private static PdfStamp RedBox(int page) => new(page, 0.1, 0.1, 0.3, 0.2)
    {
        Picture = [.. Enumerable.Repeat<byte[]>([0, 0, 255, 255], 16 * 16).SelectMany(p => p)],
        PictureWidth = 16,
        PictureHeight = 16,
    };

    [Fact]
    public void Pages_from_two_files_come_out_in_the_order_given()
    {
        var built = PdfEdit.Build([Sample("First file"), Sample("Second file")], [new(1, 0), new(0, 0)], []);
        var text = PdfText.Read(built);
        Assert.True(text.IndexOf("Second file", StringComparison.Ordinal) < text.IndexOf("First file", StringComparison.Ordinal));
        Assert.Equal(2, PdfText.Pages(built));
    }

    [Fact]
    public void A_turned_page_lies_on_its_side()
    {
        var built = PdfEdit.Build([Sample("Quote")], [new(0, 0, Turns: 1)], []);
        var (width, height) = PdfEdit.PageSizes(built)[0];
        Assert.True(width > height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_picture_lands_where_it_was_put_however_the_page_is_turned(int turns)
    {
        var built = PdfEdit.Build([Sample("Sign here")], [new(0, 0, turns)], [RedBox(0)]);
        var inside = ColourAt(built, 0, 0.25, 0.2);
        var outside = ColourAt(built, 0, 0.75, 0.8);
        Assert.True(inside.R > 200 && inside.G < 60, $"Inside the box should be red, was {inside}");
        Assert.False(outside.R > 200 && outside.G < 60, $"Away from the box shouldn't be red, was {outside}");
    }

    [Fact]
    public void Words_put_on_a_page_are_real_text()
    {
        var built = PdfEdit.Build([Sample("Application form")], [new(0, 0)], [new PdfStamp(0, 0.2, 0.5, 0, 0.03) { Text = "Pieter van Wyk\n4 November 2026" }]);
        var text = PdfText.Read(built);
        Assert.Contains("Pieter van Wyk", text, StringComparison.Ordinal);
        Assert.Contains("4 November 2026", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_smaller_copy_keeps_its_pages_and_their_size()
    {
        var two = PdfEdit.Build([Sample("One"), Sample("Two")], [new(0, 0), new(1, 0)], []);
        var smaller = PdfEdit.Smaller(two);
        Assert.Equal(2, PdfText.Pages(smaller));
        Assert.Equal((600d, 800d), PdfEdit.PageSizes(smaller)[0]);
        Assert.Equal(2, PdfEdit.Pictures(two).Count);
    }

    [Fact]
    public void A_PDF_and_a_picture_go_into_one_PDF_in_order()
    {
        var folder = Directory.CreateTempSubdirectory("webignite-pdf-");
        try
        {
            var pdf = Path.Combine(folder.FullName, "quote.pdf");
            File.WriteAllBytes(pdf, Sample("Quote page"));
            var picture = Path.Combine(folder.FullName, "slip.png");
            using (var bitmap = new SKBitmap(40, 20))
            {
                bitmap.Erase(SKColors.Blue);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(picture, png.ToArray());
            }
            var output = Path.Combine(folder.FullName, "together.pdf");
            PdfMaker.Combine([pdf, picture], output);
            var built = File.ReadAllBytes(output);
            Assert.Equal(2, PdfText.Pages(built));
            Assert.Contains("Quote page", PdfText.Read(built), StringComparison.Ordinal);
            Assert.Equal((40d, 20d), PdfEdit.PageSizes(built)[1]);
            var blue = ColourAt(built, 1, 0.5, 0.5);
            Assert.True(blue.B > 200 && blue.R < 60, $"The picture page should be blue, was {blue}");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_file_that_isnt_a_PDF_says_so()
    {
        byte[] notPdf = [.. "Not a PDF at all"u8];
        Assert.Throws<InvalidDataException>(() => PdfText.Pages(notPdf));
        Assert.Throws<IOException>(() => PdfEdit.PageSizes(notPdf));
    }
}
