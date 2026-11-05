using System.Text;
using Docnet.Core;
using Docnet.Core.Models;

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
            try
            {
                using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1, 1));
                var text = new StringBuilder();
                for (var i = 0; i < Math.Min(reader.GetPageCount(), maxPages); i++)
                {
                    using var page = reader.GetPageReader(i);
                    text.AppendLine(page.GetText());
                }
                return text.ToString();
            }
            catch (Docnet.Core.Exceptions.DocnetException e)
            {
                throw new InvalidDataException("It isn't a PDF that can be read.", e);
            }
        }
    }

    public static int Pages(byte[] pdf)
    {
        lock (PdfPages.Gate)
        {
            try
            {
                using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1, 1));
                return reader.GetPageCount();
            }
            catch (Docnet.Core.Exceptions.DocnetException e)
            {
                throw new InvalidDataException("It isn't a PDF that can be read.", e);
            }
        }
    }
}
