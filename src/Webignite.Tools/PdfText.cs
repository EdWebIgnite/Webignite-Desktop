using System.Text;

namespace Webignite.Tools;

/// <summary>
/// The words in a PDF and how many pages it has, for searching inside files and the details pane. A file named
/// .pdf that isn't one throws <see cref="InvalidDataException"/>, like any other file that can't be read, so one
/// broken file never stops a search.
/// </summary>
public static class PdfText
{
    /// <summary>The text of the first <paramref name="maxPages"/> pages. A scanned PDF has none.</summary>
    public static string Read(byte[] pdf, int maxPages = 200)
    {
        lock (PdfPages.Gate)
        {
            using var document = Pdfium.Open(pdf);
            var text = new StringBuilder();
            for (var i = 0; i < Math.Min(document.PageCount, maxPages); i++)
                text.AppendLine(Pdfium.Text(document, i));
            return text.ToString();
        }
    }

    public static int Pages(byte[] pdf)
    {
        lock (PdfPages.Gate)
        {
            using var document = Pdfium.Open(pdf);
            return document.PageCount;
        }
    }
}
