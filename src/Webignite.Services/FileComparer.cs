using System.Security.Cryptography;
using Webignite.Core.Files;

namespace Webignite.Services;

/// <summary>What comparing two files found: rows to show side by side, or just whether they match when they aren't text.</summary>
public sealed record FileComparison(IReadOnlyList<DiffRow>? Rows, bool Same, string Summary);

/// <summary>How far a search for duplicate files has got.</summary>
public sealed record DuplicateProgress(int FilesSeen, int FilesRead, string Now);

/// <summary>
/// The disk work behind Compare and the duplicate finder: reading two files or two folders to compare them with
/// the rules in Core, and finding files in a folder that are byte for byte the same. Read-only; nothing is
/// changed or removed here.
/// </summary>
public static class FileComparer
{
    /// <summary>The biggest file compared line by line. Bigger ones are only said to be the same or different.</summary>
    public const long MostTextBytes = 8 * 1024 * 1024;

    /// <summary>The most files looked at in a folder, so a whole disk picked by mistake doesn't run for an hour.</summary>
    public const int MostFiles = 200_000;

    private static readonly EnumerationOptions Inside = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>Two files: line by line when both are text of a sensible size, otherwise byte for byte.</summary>
    public static FileComparison Files(string left, string right, bool ignoreSpaces)
    {
        var (leftSize, rightSize) = (new FileInfo(left).Length, new FileInfo(right).Length);
        if (leftSize <= MostTextBytes && rightSize <= MostTextBytes)
        {
            var (leftBytes, rightBytes) = (File.ReadAllBytes(left), File.ReadAllBytes(right));
            if (TextCompare.LooksLikeText(leftBytes.AsSpan(0, Math.Min(leftBytes.Length, 8192))) && TextCompare.LooksLikeText(rightBytes.AsSpan(0, Math.Min(rightBytes.Length, 8192))))
            {
                var leftLines = TextCompare.Lines(Text(leftBytes));
                var rightLines = TextCompare.Lines(Text(rightBytes));
                if (leftLines.Length <= TextCompare.MostLines && rightLines.Length <= TextCompare.MostLines)
                {
                    var rows = TextCompare.Rows(leftLines, rightLines, ignoreSpaces);
                    return new FileComparison(rows, rows.All(r => r.Kind == DiffKind.Same), TextCompare.Summary(rows));
                }
            }
            var match = leftBytes.AsSpan().SequenceEqual(rightBytes);
            return new FileComparison(null, match, NotText(match, leftSize, rightSize));
        }
        var same = leftSize == rightSize && SameBytes(left, right);
        return new FileComparison(null, same, NotText(same, leftSize, rightSize));
    }

    private static string NotText(bool same, long leftSize, long rightSize) => same
        ? "The two files are exactly the same."
        : "The two files are different" + (leftSize == rightSize ? ", though the same size." : $": {FileSizes.Format(leftSize)} on the left, {FileSizes.Format(rightSize)} on the right.")
            + " They aren't text (or are too long), so there are no lines to show.";

    // Text as it is nearly always saved: UTF-8, or with a mark at the start that says otherwise.
    private static string Text(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Two folders, file by file. Null with why when a folder holds too many files.</summary>
    public static (IReadOnlyList<FolderRow>? Rows, string Summary) Folders(string left, string right, CancellationToken cancel = default)
    {
        var lefts = Listing(left, cancel);
        var rights = Listing(right, cancel);
        if (lefts is null || rights is null)
            return (null, $"There are more than {MostFiles:N0} files in there. Pick a folder further in.");
        var rows = FolderCompare.Rows(lefts, rights, path =>
        {
            cancel.ThrowIfCancellationRequested();
            return SameBytes(Path.Combine(left, path), Path.Combine(right, path));
        });
        return (rows, FolderCompare.Summary(rows));
    }

    private static List<FolderItem>? Listing(string folder, CancellationToken cancel)
    {
        var items = new List<FolderItem>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Inside))
        {
            cancel.ThrowIfCancellationRequested();
            if (items.Count >= MostFiles)
                return null;
            items.Add(new FolderItem(Path.GetRelativePath(folder, file.FullName), file.Length, file.LastWriteTimeUtc));
        }
        return items;
    }

    /// <summary>True when two files hold the same bytes. A file that can't be read counts as different.</summary>
    public static bool SameBytes(string left, string right)
    {
        try
        {
            using var a = File.OpenRead(left);
            using var b = File.OpenRead(right);
            if (a.Length != b.Length)
                return false;
            var (bufferA, bufferB) = (new byte[81920], new byte[81920]);
            while (true)
            {
                var readA = a.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false);
                var readB = b.ReadAtLeast(bufferB, bufferB.Length, throwOnEndOfStream: false);
                if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                    return false;
                if (readA == 0)
                    return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The files under a folder that are copies of each other. Sizes first, as only files of one size can match;
    /// then the first part of each, which rules most out cheaply; then the whole of what is left.
    /// </summary>
    public static IReadOnlyList<DuplicateSet> Duplicates(string folder, IProgress<DuplicateProgress>? progress = null, CancellationToken cancel = default)
    {
        var files = new List<(string Path, long Size)>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Inside))
        {
            cancel.ThrowIfCancellationRequested();
            if (files.Count >= MostFiles)
                break;
            files.Add((file.FullName, file.Length));
            if (files.Count % 500 == 0)
                progress?.Report(new DuplicateProgress(files.Count, 0, file.DirectoryName ?? ""));
        }
        var sizes = files.ToDictionary(f => f.Path, f => f.Size);
        var read = 0;
        string? Print(string path, bool whole)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new DuplicateProgress(files.Count, ++read, path));
            return Fingerprint(path, whole);
        }

        var sets = new List<DuplicateSet>();
        foreach (var sameSize in Core.Files.Duplicates.SameSize(files))
        {
            foreach (var sameStart in Core.Files.Duplicates.SameFingerprint(sameSize, path => Print(path, whole: false)))
            {
                // A small file was read whole the first time.
                var size = sizes[sameStart[0]];
                var same = size <= FirstPart ? [sameStart] : Core.Files.Duplicates.SameFingerprint(sameStart, path => Print(path, whole: true));
                sets.AddRange(same.Select(paths => new DuplicateSet(size, paths)));
            }
        }
        return Core.Files.Duplicates.Ordered(sets);
    }

    private const int FirstPart = 64 * 1024;

    private static string? Fingerprint(string path, bool whole)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (whole)
                return Convert.ToHexString(SHA256.HashData(file));
            var start = new byte[FirstPart];
            var read = file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
            return Convert.ToHexString(SHA256.HashData(start.AsSpan(0, read)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
