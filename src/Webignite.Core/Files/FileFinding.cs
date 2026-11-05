using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Webignite.Core.Files;

/// <summary>
/// Searching in Files: by name as you type, and optionally for words inside PDFs, Word documents and text files.
/// Every word typed must be there, in any order; letter case and accents don't matter ("cafe" finds "Café").
/// </summary>
public static partial class FileSearch
{
    /// <summary>Files bigger than this aren't opened to look inside, so a search doesn't stall on a video.</summary>
    public const long LargestToLookInside = 30 * 1024 * 1024;

    /// <summary>At most this many results, so a search over a whole drive stays quick to show.</summary>
    public const int MostResults = 500;

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private static readonly HashSet<string> PlainText = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".log", ".html", ".htm", ".css", ".js", ".json", ".xml", ".php", ".ini", ".eml", ".svg", ".rtf",
    };

    /// <summary>At most this many names are read for Ctrl+K, so a huge folder can't hold the search up.</summary>
    public const int MostIndexed = 20000;

    /// <summary>How deep below the client folder Ctrl+K looks for file names.</summary>
    public const int IndexDepth = 6;

    // Folders full of code or caches that nobody looks for a file in by hand, and that hold tens of thousands.
    private static readonly HashSet<string> NotIndexed = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "vendor", "bower_components", "bin", "obj", "dist", "build", "__pycache__", "cache", "wp-includes", "wp-admin",
    };

    /// <summary>Whether Ctrl+K skips a folder: hidden ones (a leading dot) and code and cache folders.</summary>
    public static bool SkipWhenIndexing(string folderName) => folderName.StartsWith('.') || NotIndexed.Contains(folderName);

    public static IReadOnlyList<string> Words(string? query) =>
        (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(string text, IReadOnlyList<string> words) =>
        words.Count > 0 && words.All(w => Compare.IndexOf(text, w, Loose) >= 0);

    public static bool NameMatches(string name, string? query) => Matches(name, Words(query));

    /// <summary>Files whose words Files can read: text files, Word documents (.docx) and PDFs.</summary>
    public static bool CanLookInside(string name) => IsPlainText(name) || IsWord(name) || FileKinds.IsPdf(name);

    public static bool IsPlainText(string name) => PlainText.Contains(Path.GetExtension(name));

    public static bool IsWord(string name) => Path.GetExtension(name).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    /// <summary>A short piece of the text around the first word found, on one line, for showing with the result.</summary>
    public static string? Snippet(string text, IReadOnlyList<string> words, int width = 100)
    {
        if (words.Count == 0)
            return null;
        var at = Compare.IndexOf(text, words[0], Loose);
        if (at < 0)
            return null;
        var start = Math.Max(0, at - width / 3);
        var end = Math.Min(text.Length, start + width);
        var piece = Spaces().Replace(text[start..end], " ").Trim();
        return (start > 0 ? "…" : "") + piece + (end < text.Length ? "…" : "");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>The words in a Word document (.docx), read straight from the file: it is a zip of XML.</summary>
public static partial class WordText
{
    public static string Read(Stream docx)
    {
        using var zip = new ZipArchive(docx, ZipArchiveMode.Read);
        if (zip.GetEntry("word/document.xml") is not { } body)
            return "";
        using var reader = new StreamReader(body.Open(), Encoding.UTF8);
        return FromXml(reader.ReadToEnd());
    }

    /// <summary>Paragraphs become lines, tabs spaces; everything else in the XML is dropped.</summary>
    public static string FromXml(string xml)
    {
        var text = Paragraph().Replace(xml, "\n");
        text = Tab().Replace(text, " ");
        text = Tag().Replace(text, "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    [GeneratedRegex(@"</w:p>")]
    private static partial Regex Paragraph();

    [GeneratedRegex(@"<w:(tab|br)\b[^>]*/>")]
    private static partial Regex Tab();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();
}

/// <summary>Finder's column view: which folders get a column, from where the person's folders start.</summary>
public static class FileColumns
{
    /// <summary>
    /// The folders from the start (the client folders when the folder is in them, else its drive) down to the
    /// folder itself, each one a column.
    /// </summary>
    public static IReadOnlyList<string> Chain(string folder, string? clientRoot)
    {
        var start = clientRoot is not null && Inside(folder, clientRoot) ? clientRoot : FilePaths.Crumbs(folder).FirstOrDefault().Path ?? folder;
        var chain = new List<string>();
        for (var at = folder; at is not null; at = FilePaths.Parent(at))
        {
            chain.Add(at);
            if (FileTags.Same(at, start))
                break;
        }
        chain.Reverse();
        return chain;
    }

    private static bool Inside(string folder, string root)
    {
        var a = folder.TrimEnd('\\', '/');
        var b = root.TrimEnd('\\', '/');
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) && a.Length > b.Length && a[b.Length] is '\\' or '/');
    }
}

/// <summary>Files opened lately, newest first, for Recents in the side list.</summary>
public static class FileRecents
{
    public const int Most = 40;

    public static IReadOnlyList<string> Add(IReadOnlyList<string> recents, string path) =>
        [path, .. recents.Where(p => !FileTags.Same(p, path)).Take(Most - 1)];
}

/// <summary>Names and checks for the quick actions on right-click.</summary>
public static class QuickActions
{
    private static readonly HashSet<string> Rotatable = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    public static bool CanRotate(string name) => Rotatable.Contains(Path.GetExtension(name));

    /// <summary>PDFs and pictures can go into one PDF, in the order picked.</summary>
    public static bool CanCombine(IReadOnlyList<string> names) =>
        names.Count > 1 && names.All(n => FileKinds.IsPdf(n) || CanRotate(n));

    /// <summary>"Brief.zip" for one item, else the folder's name, e.g. "Bosveld Builders.zip".</summary>
    public static string ZipName(IReadOnlyList<string> names, string folderName) =>
        (names.Count == 1 ? Path.GetFileNameWithoutExtension(names[0]) is { Length: > 0 } stem ? stem : names[0] : folderName) + ".zip";
}
