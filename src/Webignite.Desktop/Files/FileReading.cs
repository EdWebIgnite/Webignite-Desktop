using System.Text;
using Webignite.Core.Files;

namespace Webignite.Desktop.Files;

/// <summary>
/// The words in a file, for searching inside files and for Quick Look: text files as they are, Word documents
/// from their XML, PDFs through the PDF library (read from disk only when a PDF is first looked at).
/// </summary>
public static class FileReading
{
    /// <summary>The text, at most <paramref name="most"/> characters, or null when it can't be read.</summary>
    public static string? Text(string path, int most = 2_000_000)
    {
        try
        {
            var size = new FileInfo(path).Length;
            if (size > FileSearch.LargestToLookInside)
                return null;
            var name = Path.GetFileName(path);
            string text;
            if (FileSearch.IsPlainText(name))
            {
                using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[Math.Min(most, (int)Math.Min(size, int.MaxValue))];
                text = new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
            }
            else if (FileSearch.IsWord(name))
            {
                using var stream = File.OpenRead(path);
                text = WordText.Read(stream);
            }
            else if (FileKinds.IsPdf(name))
            {
                text = PdfText(File.ReadAllBytes(path));
            }
            else
            {
                return null;
            }
            return text.Length > most ? text[..most] : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or DllNotFoundException)
        {
            return null;
        }
    }

    // A method of its own, so the PDF library is only read from disk when a PDF is looked inside.
    private static string PdfText(byte[] pdf) => Webignite.Tools.PdfText.Read(pdf);
}
