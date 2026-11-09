using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Webignite.Core.Tools;

namespace Webignite.Tools;

/// <summary>
/// Putting PDFs together page by page with PDFium: pages in a new order,
/// from several files, turned, with words and a signature put on. The files given are never changed; a new PDF
/// is made each time. One at a time, as PDFium isn't safe to use from two threads at once.
/// </summary>
public static class PdfEdit
{
    /// <summary>The pages listed, from the files given, with their turns and anything stamped on, as a new PDF.</summary>
    public static byte[] Build(IReadOnlyList<byte[]> files, IReadOnlyList<PdfPage> pages, IReadOnlyList<PdfStamp> stamps)
    {
        lock (PdfPages.Gate)
        {
            Pdfium.Start();
            var opened = new List<Pdfium.Document>();
            var target = Pdfium.FPDF_CreateNewDocument();
            try
            {
                foreach (var file in files)
                    opened.Add(Open(file));
                for (var i = 0; i < pages.Count; i++)
                {
                    var page = pages[i];
                    if (!Pdfium.FPDF_ImportPages(target, opened[page.Source].Handle, (page.Page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), i))
                        throw new IOException($"Page {page.Page + 1} couldn't be copied.");
                    var loaded = Pdfium.FPDF_LoadPage(target, i);
                    try
                    {
                        var turns = (Pdfium.FPDFPage_GetRotation(loaded) + page.Turns) % 4;
                        Pdfium.FPDFPage_SetRotation(loaded, turns);
                        var onPage = stamps.Where(s => s.Page == i).ToList();
                        if (onPage.Count > 0)
                            Stamp(target, loaded, onPage, turns);
                    }
                    finally
                    {
                        Pdfium.FPDF_ClosePage(loaded);
                    }
                }
                return Pdfium.Save(target);
            }
            finally
            {
                Pdfium.FPDF_CloseDocument(target);
                foreach (var document in opened)
                    document.Dispose();
            }
        }
    }

    /// <summary>How many pages a PDF has, and the size of each as shown, in points, for laying out thumbnails.</summary>
    public static IReadOnlyList<(double Width, double Height)> PageSizes(byte[] pdf)
    {
        lock (PdfPages.Gate)
        {
            using var document = Open(pdf);
            var sizes = new List<(double, double)>();
            for (var i = 0; i < document.PageCount; i++)
            {
                if (Pdfium.FPDF_GetPageSizeByIndex(document.Handle, i, out var width, out var height) == 0)
                    throw new IOException($"Page {i + 1} couldn't be read.");
                sizes.Add((width, height));
            }
            return sizes;
        }
    }

    /// <summary>
    /// A smaller copy: each page redrawn as a picture at <paramref name="dotsPerInch"/> and saved as JPEG. Text in it
    /// can't be picked or searched afterwards, which is why it's a copy.
    /// </summary>
    public static byte[] Smaller(byte[] pdf, int dotsPerInch = 110, int quality = 70)
    {
        var sizes = PageSizes(pdf);
        var pictures = new List<(byte[] Jpeg, int Width, int Height, double PageWidth, double PageHeight)>();
        for (var i = 0; i < sizes.Count; i++)
        {
            var (width, height) = sizes[i];
            using var page = Render(pdf, i, (int)(width / 72 * dotsPerInch));
            using var image = SKImage.FromBitmap(page);
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            pictures.Add((jpeg.ToArray(), page.Width, page.Height, width, height));
        }
        return FromJpegs(pictures);
    }

    /// <summary>Each page as a PNG picture at <paramref name="dotsPerInch"/>.</summary>
    public static IReadOnlyList<byte[]> Pictures(byte[] pdf, int dotsPerInch = 150)
    {
        var sizes = PageSizes(pdf);
        var pictures = new List<byte[]>();
        for (var i = 0; i < sizes.Count; i++)
        {
            using var page = Render(pdf, i, (int)(sizes[i].Width / 72 * dotsPerInch));
            using var image = SKImage.FromBitmap(page);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            pictures.Add(png.ToArray());
        }
        return pictures;
    }

    // One page drawn as a Skia picture about this wide, white behind.
    private static SKBitmap Render(byte[] pdf, int index, int width)
    {
        lock (PdfPages.Gate)
        {
            using var document = Open(pdf);
            // Drawn on white already, so the picture can be taken as it is.
            var (raw, w, h) = Pdfium.Draw(document, index, width, width * 4);
            var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
            Marshal.Copy(raw, 0, bitmap.GetPixels(), raw.Length);
            return bitmap;
        }
    }

    // A PDF of pages each filled with a JPEG, at the page sizes given.
    private static byte[] FromJpegs(List<(byte[] Jpeg, int Width, int Height, double PageWidth, double PageHeight)> pages)
    {
        lock (PdfPages.Gate)
        {
            Pdfium.Start();
            var target = Pdfium.FPDF_CreateNewDocument();
            try
            {
                for (var i = 0; i < pages.Count; i++)
                {
                    var (jpeg, width, height, pageWidth, pageHeight) = pages[i];
                    var page = Pdfium.FPDFPage_New(target, i, pageWidth, pageHeight);
                    try
                    {
                        // The JPEG goes in as it is, so the copy really is smaller.
                        Pdfium.PutJpeg(target, page, jpeg, pageWidth, pageHeight);
                    }
                    finally
                    {
                        Pdfium.FPDF_ClosePage(page);
                    }
                }
                return Pdfium.Save(target);
            }
            finally
            {
                Pdfium.FPDF_CloseDocument(target);
            }
        }
    }

    private static Pdfium.Document Open(byte[] file)
    {
        try
        {
            return Pdfium.Open(file);
        }
        catch (InvalidDataException e)
        {
            throw new IOException("One of the PDFs couldn't be read. It may be locked with a password or damaged.", e);
        }
    }

    // Words and pictures on a page, placed where they show on the page as it sits (turned or not).
    private static void Stamp(IntPtr document, IntPtr page, IReadOnlyList<PdfStamp> stamps, int turns)
    {
        // The page as stored, before any turn: its MediaBox.
        if (!Pdfium.FPDFPage_GetMediaBox(page, out var left, out var bottom, out var right, out var top))
            throw new IOException("A page's size couldn't be read.");
        var width = (double)(right - left);
        var height = (double)(top - bottom);
        // The page as shown: turned a quarter, it shows its height across.
        var (shownWidth, shownHeight) = turns % 2 == 1 ? (height, width) : (width, height);
        foreach (var stamp in stamps)
        {
            if (stamp.Picture is { } picture)
            {
                var w = stamp.Width * shownWidth;
                var h = stamp.Height * shownHeight;
                var (x, y) = PdfPlan.ToPdf(stamp.X, stamp.Y + stamp.Height, width, height, turns);
                using var bitmap = new SKBitmap(stamp.PictureWidth, stamp.PictureHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                Marshal.Copy(picture, 0, bitmap.GetPixels(), picture.Length);
                var image = Picture(document, page, bitmap.Copy());
                Place(image, x, y, w, h, turns);
                Pdfium.FPDFPage_InsertObject(page, image);
            }
            else if (stamp.Text is { Length: > 0 } text)
            {
                var size = stamp.Height * shownHeight;
                var font = Pdfium.FPDFText_LoadStandardFont(document, "Helvetica");
                // The baseline sits about four fifths of the way down the line.
                var (x, y) = PdfPlan.ToPdf(stamp.X, stamp.Y + stamp.Height * 0.8, width, height, turns);
                foreach (var (line, index) in text.Split('\n').Select((l, i) => (l, i)))
                {
                    var words = Pdfium.FPDFPageObj_CreateTextObj(document, font, (float)size);
                    var wide = Encoding.Unicode.GetBytes(line.TrimEnd('\r') + "\0");
                    var handle = GCHandle.Alloc(wide, GCHandleType.Pinned);
                    try
                    {
                        Pdfium.FPDFText_SetText(words, handle.AddrOfPinnedObject());
                    }
                    finally
                    {
                        handle.Free();
                    }
                    Pdfium.FPDFPageObj_SetFillColor(words, 20, 20, 20, 255);
                    var (lx, ly) = turns switch
                    {
                        1 => (x + index * size * 1.2, y),
                        2 => (x, y + index * size * 1.2),
                        3 => (x - index * size * 1.2, y),
                        _ => (x, y - index * size * 1.2),
                    };
                    Turn(words, lx, ly, turns);
                    Pdfium.FPDFPage_InsertObject(page, words);
                }
            }
        }
        Pdfium.FPDFPage_GenerateContent(page);
    }

    // A picture object on a page, from a Skia picture (four bytes a pixel), which it then lets go.
    private static IntPtr Picture(IntPtr document, IntPtr page, SKBitmap bitmap)
    {
        using (bitmap)
        {
            var image = Pdfium.FPDFPageObj_NewImageObj(document);
            var pdfBitmap = Pdfium.FPDFBitmap_CreateEx(bitmap.Width, bitmap.Height, 4 /* BGRA */, bitmap.GetPixels(), bitmap.RowBytes);
            try
            {
                var pages = new[] { page };
                if (!Pdfium.FPDFImageObj_SetBitmap(pages, 1, image, pdfBitmap))
                    throw new IOException("A picture couldn't be put on the page.");
            }
            finally
            {
                Pdfium.FPDFBitmap_Destroy(pdfBitmap);
            }
            return image;
        }
    }

    // An image's box: its bottom-left corner as shown at (x, y), sized as shown, turned with the page.
    private static void Place(IntPtr image, double x, double y, double width, double height, int turns)
    {
        switch (turns)
        {
            case 1:
                Pdfium.FPDFImageObj_SetMatrix(image, 0, width, -height, 0, x, y);
                break;
            case 2:
                Pdfium.FPDFImageObj_SetMatrix(image, -width, 0, 0, -height, x, y);
                break;
            case 3:
                Pdfium.FPDFImageObj_SetMatrix(image, 0, -width, height, 0, x, y);
                break;
            default:
                Pdfium.FPDFImageObj_SetMatrix(image, width, 0, 0, height, x, y);
                break;
        }
    }

    // Text set at (x, y) and turned with the page so it reads upright as shown.
    private static void Turn(IntPtr words, double x, double y, int turns)
    {
        var (a, b, c, d) = turns switch
        {
            1 => (0d, 1d, -1d, 0d),
            2 => (-1d, 0d, 0d, -1d),
            3 => (0d, -1d, 1d, 0d),
            _ => (1d, 0d, 0d, 1d),
        };
        Pdfium.FPDFPageObj_Transform(words, a, b, c, d, x, y);
    }
}
