using System.Globalization;

namespace Webignite.Core.Files;

public enum DiffKind
{
    Same,
    /// <summary>Only in the left file.</summary>
    Removed,
    /// <summary>Only in the right file.</summary>
    Added,
    /// <summary>A line on the left with a different line in its place on the right.</summary>
    Changed,
    /// <summary>A run of lines that are the same, folded away when only the differences are shown.</summary>
    Skipped,
}

/// <summary>One row of two files side by side. A side with no line there has no number and no text.</summary>
public sealed record DiffRow(DiffKind Kind, int? LeftNumber, string? Left, int? RightNumber, string? Right);

/// <summary>Two text files compared line by line.</summary>
public static class TextCompare
{
    /// <summary>The longest file compared line by line. Longer ones are only said to be the same or different.</summary>
    public const int MostLines = 50_000;

    // How far apart two files may be before the search for the best match is given up for a plainer answer.
    private const int MostEdits = 2_000;

    /// <summary>A file's text as lines, whatever line endings it uses.</summary>
    public static string[] Lines(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n').Split('\n');

    /// <summary>True when the bytes at the start of a file look like text: no zero bytes, as pictures and programs have.</summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> start) => !start.Contains((byte)0);

    /// <summary>
    /// The two files side by side: lines that match on the same row, a line only one side has on a row of its
    /// own, and a line that was changed beside what it became.
    /// </summary>
    /// <param name="ignoreSpaces">Lines that differ only in spaces and tabs at their ends count as the same.</param>
    public static IReadOnlyList<DiffRow> Rows(IReadOnlyList<string> left, IReadOnlyList<string> right, bool ignoreSpaces = false)
    {
        bool Match(int l, int r) => ignoreSpaces ? left[l].AsSpan().Trim().SequenceEqual(right[r].AsSpan().Trim()) : string.Equals(left[l], right[r], StringComparison.Ordinal);

        var rows = new List<DiffRow>();
        // What the two start and end with in common needs no searching.
        var start = 0;
        while (start < left.Count && start < right.Count && Match(start, start))
            start++;
        var endLeft = left.Count;
        var endRight = right.Count;
        while (endLeft > start && endRight > start && Match(endLeft - 1, endRight - 1))
        {
            endLeft--;
            endRight--;
        }
        for (var i = 0; i < start; i++)
            rows.Add(new DiffRow(DiffKind.Same, i + 1, left[i], i + 1, right[i]));

        var steps = Middle(start, endLeft, start, endRight, Match);
        // Lines taken out and put in at the same place are a change: they are shown side by side.
        for (var i = 0; i < steps.Count;)
        {
            if (steps[i].Kind == DiffKind.Same)
            {
                rows.Add(new DiffRow(DiffKind.Same, steps[i].Left + 1, left[steps[i].Left], steps[i].Right + 1, right[steps[i].Right]));
                i++;
                continue;
            }
            var removed = new List<int>();
            var added = new List<int>();
            for (; i < steps.Count && steps[i].Kind != DiffKind.Same; i++)
                (steps[i].Kind == DiffKind.Removed ? removed : added).Add(steps[i].Kind == DiffKind.Removed ? steps[i].Left : steps[i].Right);
            var both = Math.Min(removed.Count, added.Count);
            for (var k = 0; k < both; k++)
                rows.Add(new DiffRow(DiffKind.Changed, removed[k] + 1, left[removed[k]], added[k] + 1, right[added[k]]));
            for (var k = both; k < removed.Count; k++)
                rows.Add(new DiffRow(DiffKind.Removed, removed[k] + 1, left[removed[k]], null, null));
            for (var k = both; k < added.Count; k++)
                rows.Add(new DiffRow(DiffKind.Added, null, null, added[k] + 1, right[added[k]]));
        }

        for (int l = endLeft, r = endRight; l < left.Count; l++, r++)
            rows.Add(new DiffRow(DiffKind.Same, l + 1, left[l], r + 1, right[r]));
        return rows;
    }

    private readonly record struct Step(DiffKind Kind, int Left, int Right);

    // The shortest list of lines to take out and put in that turns one middle into the other (Myers' method):
    // walk outwards one edit at a time, always as far along matching lines as each path can get.
    private static List<Step> Middle(int leftFrom, int leftTo, int rightFrom, int rightTo, Func<int, int, bool> match)
    {
        var n = leftTo - leftFrom;
        var m = rightTo - rightFrom;
        var steps = new List<Step>();
        if (n == 0 || m == 0)
            return Plain(leftFrom, leftTo, rightFrom, rightTo);
        var most = Math.Min(n + m, MostEdits);
        var reach = new int[2 * most + 2];
        var trace = new List<int[]>();
        var found = -1;
        for (var d = 0; d <= most && found < 0; d++)
        {
            // Only the part of the row this round can read is kept: places -d to d.
            trace.Add(reach.AsSpan(most - d, 2 * d + 1).ToArray());
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && reach[most + k - 1] < reach[most + k + 1]) ? reach[most + k + 1] : reach[most + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && match(leftFrom + x, rightFrom + y))
                {
                    x++;
                    y++;
                }
                reach[most + k] = x;
                if (x >= n && y >= m)
                {
                    found = d;
                    break;
                }
            }
        }
        // Too different to be worth matching up line by line: all of one, then all of the other.
        if (found < 0)
            return Plain(leftFrom, leftTo, rightFrom, rightTo);

        // Back along the path that got there, from the end to the start.
        var (atX, atY) = (n, m);
        for (var d = found; d > 0; d--)
        {
            var before = trace[d];
            var k = atX - atY;
            var fromK = k == -d || (k != d && before[d + k - 1] < before[d + k + 1]) ? k + 1 : k - 1;
            var fromX = before[d + fromK];
            var fromY = fromX - fromK;
            while (atX > fromX && atY > fromY)
            {
                atX--;
                atY--;
                steps.Add(new Step(DiffKind.Same, leftFrom + atX, rightFrom + atY));
            }
            if (atX == fromX)
            {
                atY--;
                steps.Add(new Step(DiffKind.Added, -1, rightFrom + atY));
            }
            else
            {
                atX--;
                steps.Add(new Step(DiffKind.Removed, leftFrom + atX, -1));
            }
        }
        while (atX > 0 && atY > 0)
        {
            atX--;
            atY--;
            steps.Add(new Step(DiffKind.Same, leftFrom + atX, rightFrom + atY));
        }
        steps.Reverse();
        return steps;
    }

    private static List<Step> Plain(int leftFrom, int leftTo, int rightFrom, int rightTo)
    {
        var steps = new List<Step>();
        for (var l = leftFrom; l < leftTo; l++)
            steps.Add(new Step(DiffKind.Removed, l, -1));
        for (var r = rightFrom; r < rightTo; r++)
            steps.Add(new Step(DiffKind.Added, -1, r));
        return steps;
    }

    /// <summary>
    /// Only what differs, with a few lines either side to show where; each long run of matching lines becomes
    /// one row that says how many were left out.
    /// </summary>
    public static IReadOnlyList<DiffRow> OnlyChanges(IReadOnlyList<DiffRow> rows, int around = 3)
    {
        var keep = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == DiffKind.Same)
                continue;
            for (var j = Math.Max(0, i - around); j <= Math.Min(rows.Count - 1, i + around); j++)
                keep[j] = true;
        }
        var shown = new List<DiffRow>();
        for (var i = 0; i < rows.Count;)
        {
            if (keep[i])
            {
                shown.Add(rows[i++]);
                continue;
            }
            var from = i;
            while (i < rows.Count && !keep[i])
                i++;
            var words = i - from == 1 ? "1 line the same" : $"{(i - from).ToString("N0", CultureInfo.InvariantCulture)} lines the same";
            shown.Add(new DiffRow(DiffKind.Skipped, null, words, null, null));
        }
        return shown;
    }

    /// <summary>"The two files are the same." or "7 lines differ: 3 changed, 2 only on the left, 2 only on the right."</summary>
    public static string Summary(IReadOnlyList<DiffRow> rows)
    {
        var changed = rows.Count(r => r.Kind == DiffKind.Changed);
        var removed = rows.Count(r => r.Kind == DiffKind.Removed);
        var added = rows.Count(r => r.Kind == DiffKind.Added);
        var all = changed + removed + added;
        if (all == 0)
            return "The two files are the same.";
        var parts = new List<string>();
        if (changed > 0)
            parts.Add($"{changed:N0} changed");
        if (removed > 0)
            parts.Add($"{removed:N0} only on the left");
        if (added > 0)
            parts.Add($"{added:N0} only on the right");
        return (all == 1 ? "1 line differs: " : $"{all:N0} lines differ: ") + string.Join(", ", parts) + ".";
    }
}

/// <summary>A file inside a folder being compared: where it is under the folder, its size and when it was last changed.</summary>
public sealed record FolderItem(string RelativePath, long Size, DateTime Modified);

public enum FolderDiff
{
    Same,
    OnlyLeft,
    OnlyRight,
    Different,
}

public sealed record FolderRow(string Path, FolderDiff Kind, FolderItem? Left, FolderItem? Right);

/// <summary>Two folders compared file by file, by where each file sits under its folder.</summary>
public static class FolderCompare
{
    /// <summary>
    /// A row for every file in either folder, in path order. Files of different sizes differ; files of the same
    /// size and time are taken as the same; the rest are read, through <paramref name="sameContent"/>.
    /// </summary>
    public static IReadOnlyList<FolderRow> Rows(IEnumerable<FolderItem> left, IEnumerable<FolderItem> right, Func<string, bool> sameContent)
    {
        var lefts = left.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var rights = right.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var rows = new List<FolderRow>();
        foreach (var path in lefts.Keys.Union(rights.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            lefts.TryGetValue(path, out var l);
            rights.TryGetValue(path, out var r);
            var kind = l is null ? FolderDiff.OnlyRight
                : r is null ? FolderDiff.OnlyLeft
                : l.Size != r.Size ? FolderDiff.Different
                : l.Modified == r.Modified || sameContent(path) ? FolderDiff.Same : FolderDiff.Different;
            rows.Add(new FolderRow(path, kind, l, r));
        }
        return rows;
    }

    public static string Words(FolderDiff kind) => kind switch
    {
        FolderDiff.OnlyLeft => "Only on the left",
        FolderDiff.OnlyRight => "Only on the right",
        FolderDiff.Different => "Different",
        _ => "The same",
    };

    /// <summary>"The two folders hold the same 42 files." or "5 of 42 files differ: 2 different, 2 only on the left, 1 only on the right."</summary>
    public static string Summary(IReadOnlyList<FolderRow> rows)
    {
        var different = rows.Count(r => r.Kind == FolderDiff.Different);
        var onlyLeft = rows.Count(r => r.Kind == FolderDiff.OnlyLeft);
        var onlyRight = rows.Count(r => r.Kind == FolderDiff.OnlyRight);
        var all = different + onlyLeft + onlyRight;
        if (rows.Count == 0)
            return "Both folders are empty.";
        if (all == 0)
            return rows.Count == 1 ? "The two folders hold the same file." : $"The two folders hold the same {rows.Count:N0} files.";
        var parts = new List<string>();
        if (different > 0)
            parts.Add($"{different:N0} different");
        if (onlyLeft > 0)
            parts.Add($"{onlyLeft:N0} only on the left");
        if (onlyRight > 0)
            parts.Add($"{onlyRight:N0} only on the right");
        return $"{all:N0} of {rows.Count:N0} files differ: " + string.Join(", ", parts) + ".";
    }
}

/// <summary>Files that are byte for byte the same.</summary>
public sealed record DuplicateSet(long Size, IReadOnlyList<string> Paths)
{
    /// <summary>What removing every copy but one would free.</summary>
    public long Spare => Size * (Paths.Count - 1);
}

public static class Duplicates
{
    /// <summary>Files that share a size with another: only these can be copies, so only these are read. Empty files are left out.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> SameSize(IEnumerable<(string Path, long Size)> files) =>
        [.. files.Where(f => f.Size > 0).GroupBy(f => f.Size).Where(g => g.Count() > 1).Select(g => (IReadOnlyList<string>)[.. g.Select(f => f.Path)])];

    /// <summary>
    /// Splits files of one size into sets with the same fingerprint (a digest of their content). A file whose
    /// fingerprint is null couldn't be read and is left out.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> SameFingerprint(IReadOnlyList<string> paths, Func<string, string?> fingerprint) =>
        [.. paths.Select(path => (path, print: fingerprint(path))).Where(f => f.print is not null).GroupBy(f => f.print)
            .Where(g => g.Count() > 1).Select(g => (IReadOnlyList<string>)[.. g.Select(f => f.path)])];

    /// <summary>The sets in the order that frees the most first, each set's files by path.</summary>
    public static IReadOnlyList<DuplicateSet> Ordered(IEnumerable<DuplicateSet> sets) =>
        [.. sets.Select(s => s with { Paths = [.. s.Paths.Order(StringComparer.OrdinalIgnoreCase)] }).OrderByDescending(s => s.Spare).ThenBy(s => s.Paths[0], StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Which copies to offer for removal: all but one in each set. The one kept is the one nearest the top of the
    /// folder (the shortest path), and of those the first by name, so copies buried in "Copy of" folders go.
    /// </summary>
    public static IReadOnlyList<string> Extras(DuplicateSet set)
    {
        var keep = set.Paths.OrderBy(p => p.Count(c => c is '/' or '\\')).ThenBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First();
        return [.. set.Paths.Where(p => p != keep)];
    }

    /// <summary>
    /// The ticked files that may go: never every copy in a set. When all of a set are ticked, the one
    /// <see cref="Extras"/> would keep stays.
    /// </summary>
    public static IReadOnlyList<string> SafeToRemove(DuplicateSet set, IReadOnlyCollection<string> ticked)
    {
        var wanted = set.Paths.Where(ticked.Contains).ToList();
        return wanted.Count < set.Paths.Count ? wanted : Extras(set);
    }

    /// <summary>"14 files are copies of others, in 5 sets. Removing the extra copies would free 340 MB."</summary>
    public static string Summary(IReadOnlyList<DuplicateSet> sets)
    {
        if (sets.Count == 0)
            return "No two files in there are the same.";
        var extra = sets.Sum(s => s.Paths.Count - 1);
        return (extra == 1 ? "1 file is a copy of another" : $"{extra:N0} files are copies of others")
            + (sets.Count == 1 ? "" : $", in {sets.Count:N0} sets")
            + $". Removing the extra {(extra == 1 ? "copy" : "copies")} would free {FileSizes.Format(sets.Sum(s => s.Spare))}.";
    }
}
