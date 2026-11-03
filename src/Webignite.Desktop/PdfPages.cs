using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Docnet.Core;
using Docnet.Core.Models;

namespace Webignite.Desktop;

/// <summary>
/// Draws the pages of a PDF as pictures for the attachment preview, with PDFium (a PDF library, not a browser).
/// One PDF at a time, as PDFium isn't safe to use from two threads at once.
/// </summary>
public static class PdfPages
{
    private static readonly Lock Gate = new();

    /// <summary>The pages, at most <paramref name="maxPages"/>, about <paramref name="width"/> pixels wide, and the page count.</summary>
    public static (IReadOnlyList<Bitmap> Pages, int Count) Render(byte[] pdf, int width, int maxPages)
    {
        lock (Gate)
        {
            using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(width, width * 3));
            var count = reader.GetPageCount();
            var pages = new List<Bitmap>();
            for (var i = 0; i < Math.Min(count, maxPages); i++)
            {
                using var page = reader.GetPageReader(i);
                var raw = page.GetImage();
                var (w, h) = (page.GetPageWidth(), page.GetPageHeight());
                var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                using (var buffer = bitmap.Lock())
                {
                    for (var row = 0; row < h; row++)
                        System.Runtime.InteropServices.Marshal.Copy(raw, row * w * 4, buffer.Address + row * buffer.RowBytes, w * 4);
                }
                pages.Add(bitmap);
            }
            return (pages, count);
        }
    }
}
