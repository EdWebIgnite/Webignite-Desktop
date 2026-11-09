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
            Pdfium.Start();
            // The pages go into a new PDF that PDFium made itself: it can always add pages to one of its own, even
            // when it can't to some it opens (ones made by hand or by older programs).
            var target = Pdfium.FPDF_CreateNewDocument();
            try
            {
                foreach (var part in parts)
                {
                    try
                    {
                        using var source = Pdfium.Open(part);
                        if (!Pdfium.FPDF_ImportPages(target, source.Handle, null, Pdfium.FPDF_GetPageCount(target)))
                            throw new IOException("One of the PDFs couldn't be copied in.");
                    }
                    catch (InvalidDataException e)
                    {
                        throw new IOException("One of the PDFs couldn't be read. It may be locked with a password.", e);
                    }
                }
                combined = Pdfium.Save(target);
            }
            finally
            {
                Pdfium.FPDF_CloseDocument(target);
            }
        }
        File.WriteAllBytes(output, combined);
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
            Pdfium.Start();
            var target = Pdfium.FPDF_CreateNewDocument();
            try
            {
                // A point for each pixel, as before: the page is the picture's size.
                var page = Pdfium.FPDFPage_New(target, 0, upright.Width, upright.Height);
                try
                {
                    Pdfium.PutJpeg(target, page, jpeg.ToArray(), upright.Width, upright.Height);
                }
                finally
                {
                    Pdfium.FPDF_ClosePage(page);
                }
                return Pdfium.Save(target);
            }
            catch (IOException e)
            {
                throw new IOException($"Couldn't put {Path.GetFileName(path)} into the PDF.", e);
            }
            finally
            {
                Pdfium.FPDF_CloseDocument(target);
            }
        }
    }
}
