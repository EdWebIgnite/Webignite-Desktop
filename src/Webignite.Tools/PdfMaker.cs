using Docnet.Core;
using Docnet.Core.Editors;
using SkiaSharp;

namespace Webignite.Tools;

/// <summary>Puts PDFs and pictures together into one PDF, in the order given. The originals stay as they are.</summary>
public static class PdfMaker
{
    public static void Combine(IReadOnlyList<string> paths, string output)
    {
        var parts = new List<byte[]>();
        foreach (var path in paths)
            parts.Add(Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? File.ReadAllBytes(path) : PictureAsPdf(path));
        byte[] combined;
        lock (PdfPages.Gate)
        {
            try
            {
                // PDFium can't add pages to some PDFs it opens (ones made by hand or by older programs), but it can
                // always add them to one it made itself. So the pages go after a blank page, which is then cut off.
                var seed = Blank();
                var merged = DocLib.Instance.Merge([seed, .. parts]);
                using var reader = DocLib.Instance.GetDocReader(merged, new Docnet.Core.Models.PageDimensions(1, 1));
                combined = DocLib.Instance.Split(merged, 1, reader.GetPageCount() - 1);
            }
            catch (Docnet.Core.Exceptions.DocnetException e)
            {
                throw new IOException("One of the PDFs couldn't be read. It may be locked with a password.", e);
            }
        }
        File.WriteAllBytes(output, combined);
    }

    private static byte[] Blank()
    {
        using var white = new SKBitmap(8, 8);
        white.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(white);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return DocLib.Instance.JpegToPdf([new JpegImage { Bytes = jpeg.ToArray(), Width = 8, Height = 8 }]);
    }

    /// <summary>A picture as a one-page PDF: turned the right way up and saved as a JPEG inside it.</summary>
    public static byte[] PictureAsPdf(string path)
    {
        using var upright = PictureTools.Upright(path);
        using var image = SKImage.FromBitmap(upright);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90)
            ?? throw new IOException($"Couldn't read the picture in {Path.GetFileName(path)}.");
        lock (PdfPages.Gate)
        {
            try
            {
                return DocLib.Instance.JpegToPdf([new JpegImage { Bytes = jpeg.ToArray(), Width = upright.Width, Height = upright.Height }]);
            }
            catch (Docnet.Core.Exceptions.DocnetException e)
            {
                throw new IOException($"Couldn't put {Path.GetFileName(path)} into the PDF.", e);
            }
        }
    }
}
