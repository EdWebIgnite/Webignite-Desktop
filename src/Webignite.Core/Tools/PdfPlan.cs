using System.Globalization;

namespace Webignite.Core.Tools;

/// <summary>One page of the PDF being put together: which file it comes from, which page, and how far turned.</summary>
/// <param name="Source">The file, as its place in the list of files opened.</param>
/// <param name="Page">The page in that file, from 0.</param>
/// <param name="Turns">Quarter turns to the right on top of how the page already sits, 0 to 3.</param>
public sealed record PdfPage(int Source, int Page, int Turns = 0);

/// <summary>Words or a picture put on a page: filled-in form text, a date, or a signature.</summary>
/// <param name="Page">The page in the finished PDF, from 0.</param>
/// <param name="X">Left edge, as a share of the page's width as shown (0 to 1).</param>
/// <param name="Y">Top edge, as a share of the page's height as shown (0 to 1).</param>
/// <param name="Width">As a share of the width shown; for text, ignored.</param>
/// <param name="Height">As a share of the height shown; for text, the size of the letters.</param>
public sealed record PdfStamp(int Page, double X, double Y, double Width, double Height)
{
    public string? Text { get; init; }

    /// <summary>A picture (the signature): BGRA, four bytes a pixel, top row first.</summary>
    public byte[]? Picture { get; init; }

    public int PictureWidth { get; init; }
    public int PictureHeight { get; init; }
}

/// <summary>Rules for the PDF tools: moving, turning and removing pages, page ranges, and placing on a turned page.</summary>
public static class PdfPlan
{
    public static IReadOnlyList<PdfPage> Move(IReadOnlyList<PdfPage> pages, int from, int to)
    {
        if (from < 0 || from >= pages.Count)
            return pages;
        var list = pages.ToList();
        var page = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(to, 0, list.Count), page);
        return list;
    }

    /// <summary>Moves several pages (in the order they sit) to land before the page that was at <paramref name="before"/>.</summary>
    public static IReadOnlyList<PdfPage> MoveMany(IReadOnlyList<PdfPage> pages, IReadOnlyCollection<int> picked, int before)
    {
        var moving = pages.Where((_, i) => picked.Contains(i)).ToList();
        var staying = pages.Where((_, i) => !picked.Contains(i)).ToList();
        var at = Math.Clamp(before - picked.Count(i => i < before), 0, staying.Count);
        staying.InsertRange(at, moving);
        return staying;
    }

    public static IReadOnlyList<PdfPage> Turn(IReadOnlyList<PdfPage> pages, IReadOnlyCollection<int> picked, int quarterTurns) =>
        [.. pages.Select((p, i) => picked.Contains(i) ? p with { Turns = ((p.Turns + quarterTurns) % 4 + 4) % 4 } : p)];

    public static IReadOnlyList<PdfPage> Remove(IReadOnlyList<PdfPage> pages, IReadOnlyCollection<int> picked) =>
        [.. pages.Where((_, i) => !picked.Contains(i))];

    /// <summary>
    /// Page numbers as people write them, "1-3, 5, 8-", into pages from 0, in the order written. Null with why
    /// when it can't be read or runs past the end.
    /// </summary>
    public static (IReadOnlyList<int>? Pages, string? Problem) Range(string? text, int count)
    {
        var pages = new List<int>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split('-', StringSplitOptions.TrimEntries);
            if (ends.Length > 2 || !int.TryParse(ends[0].Length == 0 ? "1" : ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first))
                return (null, $"\"{part}\" isn't a page or a range like 2-5.");
            var last = first;
            if (ends.Length == 2 && !int.TryParse(ends[1].Length == 0 ? count.ToString(CultureInfo.InvariantCulture) : ends[1], NumberStyles.None, CultureInfo.InvariantCulture, out last))
                return (null, $"\"{part}\" isn't a page or a range like 2-5.");
            if (first < 1 || last < first || last > count)
                return (null, count == 1 ? "There's only 1 page." : $"Pick pages from 1 to {count}.");
            for (var p = first; p <= last; p++)
                pages.Add(p - 1);
        }
        return pages.Count == 0 ? (null, "Type the pages, e.g. 1-3, 5.") : (pages, null);
    }

    /// <summary>Pages in groups of so many, e.g. every 2 pages of 5 gives 1-2, 3-4, 5.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> Every(int count, int size) =>
        [.. Enumerable.Range(0, count).Chunk(Math.Max(1, size)).Select(chunk => (IReadOnlyList<int>)chunk)];

    /// <summary>
    /// A point on a page as shown (shares of its shown width and height, from the top left) in PDF points from the
    /// bottom left of the page as stored, for a page that sits turned by <paramref name="turns"/> quarters to the right.
    /// </summary>
    public static (double X, double Y) ToPdf(double shownX, double shownY, double storedWidth, double storedHeight, int turns) =>
        (((turns % 4) + 4) % 4) switch
        {
            1 => (shownY * storedWidth, shownX * storedHeight),
            2 => ((1 - shownX) * storedWidth, shownY * storedHeight),
            3 => ((1 - shownY) * storedWidth, (1 - shownX) * storedHeight),
            _ => (shownX * storedWidth, (1 - shownY) * storedHeight),
        };

    /// <summary>"Quote (pages 1-3).pdf", "Quote (page 2).pdf": the name for pages taken out of a PDF.</summary>
    public static string PartName(string name, IReadOnlyList<int> pages)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        if (pages.Count == 1)
            return $"{stem} (page {pages[0] + 1}).pdf";
        var straight = pages.Zip(pages.Skip(1)).All(p => p.Second == p.First + 1);
        return straight ? $"{stem} (pages {pages[0] + 1}-{pages[^1] + 1}).pdf" : $"{stem} (pages {string.Join(", ", pages.Select(p => p + 1))}).pdf";
    }
}
