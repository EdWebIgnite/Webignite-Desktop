using System.Globalization;

namespace Webignite.Core.Files;

/// <summary>What the Show button keeps in the list: a kind of file.</summary>
public enum KindFilter
{
    All,
    Folders,
    Pictures,
    Pdfs,
    Documents,
    Videos,
    Zips,
}

/// <summary>What the Show button keeps in the list: when it was last changed.</summary>
public enum DateFilter
{
    Any,
    Today,
    LastWeek,
    LastMonth,
    ThisYear,
    OlderThanAYear,
}

/// <summary>Filters by kind and date, on top of the tag filter.</summary>
public static class FileFilters
{
    public static bool Keeps(FileEntry entry, KindFilter kind, DateFilter date, DateOnly today) =>
        KindKeeps(entry, kind) && DateKeeps(DateOnly.FromDateTime(entry.Modified), date, today);

    private static bool KindKeeps(FileEntry entry, KindFilter kind) => kind switch
    {
        KindFilter.Folders => entry.IsFolder,
        KindFilter.Pictures => !entry.IsFolder && entry.Kind == "Picture",
        KindFilter.Pdfs => !entry.IsFolder && entry.Kind == "PDF",
        KindFilter.Documents => !entry.IsFolder && entry.Kind is "Word document" or "Spreadsheet" or "Presentation" or "Text",
        KindFilter.Videos => !entry.IsFolder && entry.Kind == "Video",
        KindFilter.Zips => !entry.IsFolder && entry.Kind == "Zip",
        _ => true,
    };

    private static bool DateKeeps(DateOnly day, DateFilter date, DateOnly today) => date switch
    {
        DateFilter.Today => day >= today,
        DateFilter.LastWeek => day > today.AddDays(-7),
        DateFilter.LastMonth => day > today.AddDays(-30),
        DateFilter.ThisYear => day.Year == today.Year,
        DateFilter.OlderThanAYear => day <= today.AddYears(-1),
        _ => true,
    };

    public static string Name(KindFilter kind) => kind switch
    {
        KindFilter.Folders => "Folders",
        KindFilter.Pictures => "Pictures",
        KindFilter.Pdfs => "PDFs",
        KindFilter.Documents => "Documents",
        KindFilter.Videos => "Videos",
        KindFilter.Zips => "Zips",
        _ => "All kinds",
    };

    public static string Name(DateFilter date) => date switch
    {
        DateFilter.Today => "Today",
        DateFilter.LastWeek => "Last 7 days",
        DateFilter.LastMonth => "Last 30 days",
        DateFilter.ThisYear => "This year",
        DateFilter.OlderThanAYear => "Older than a year",
        _ => "Any time",
    };

    /// <summary>The Show button's words: "Show", or what it keeps, e.g. "Pictures, last 7 days".</summary>
    public static string Label(KindFilter kind, DateFilter date) => (kind, date) switch
    {
        (KindFilter.All, DateFilter.Any) => "Show",
        (_, DateFilter.Any) => Name(kind),
        (KindFilter.All, _) => Name(date),
        _ => $"{Name(kind)}, {Name(date).ToLowerInvariant()}",
    };
}

/// <summary>What is taking space in a folder: each item's share of the whole, biggest first.</summary>
public static class FolderSpace
{
    public sealed record Share(string Path, long Size, double Part);

    public static IReadOnlyList<Share> Shares(IEnumerable<(string Path, long Size)> sizes)
    {
        var list = sizes.ToList();
        var total = list.Sum(s => s.Size);
        return [.. list.OrderByDescending(s => s.Size).Select(s => new Share(s.Path, s.Size, total == 0 ? 0 : (double)s.Size / total))];
    }
}

/// <summary>
/// Renaming many files at once: find and replace in the names, then (if asked) a name with a number, e.g.
/// "Site visit 01.jpg", "Site visit 02.jpg". The extension stays as it was.
/// </summary>
public static class BulkRename
{
    public sealed record Options
    {
        public string Find { get; init; } = "";
        public string Replace { get; init; } = "";
        public bool MatchCase { get; init; }

        /// <summary>When set, every name becomes this and a number, in the order given.</summary>
        public string? NumberedName { get; init; }

        public int StartAt { get; init; } = 1;

        /// <summary>At least this many digits: 2 gives 01, 02 … 10.</summary>
        public int Digits { get; init; } = 2;
    }

    public sealed record Change(string From, string To, string? Problem)
    {
        public bool Changes => From != To;
    }

    /// <summary>
    /// The new name for each, in order, with what is wrong with any: a name Windows won't take, two the same, or
    /// one already used by something else in the folder.
    /// </summary>
    public static IReadOnlyList<Change> Plan(IReadOnlyList<string> names, Options options, IEnumerable<string> others)
    {
        var digits = Math.Max(options.Digits, (options.StartAt + names.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
        var planned = names.Select((name, i) =>
        {
            var extension = Path.GetExtension(name);
            var stem = name[..^extension.Length];
            if (options.Find.Length > 0)
                stem = stem.Replace(options.Find, options.Replace, options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(options.NumberedName))
                stem = options.NumberedName.Trim() + " " + (options.StartAt + i).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
            return stem.Trim() + extension;
        }).ToList();
        var taken = others.Where(o => !names.Contains(o, StringComparer.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var counts = planned.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return [.. names.Select((name, i) =>
        {
            var to = planned[i];
            var problem = FileNames.Problem(to)
                ?? (counts[to] > 1 ? "Two would get this name." : null)
                ?? (taken.Contains(to) ? "Something else here already has this name." : null);
            return new Change(name, to, problem);
        })];
    }

    public static bool CanApply(IReadOnlyList<Change> plan) => plan.Any(c => c.Changes) && plan.All(c => c.Problem is null);
}

/// <summary>When a copy or move goes to the copy queue, with progress and Pause, rather than happening at once.</summary>
public static class CopyRules
{
    public const long BigBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Folders and anything over 50 MB go to the queue; a move on the same drive is only a rename, so it is
    /// always at once.
    /// </summary>
    public static bool Queue(bool anyFolder, long fileBytes, bool move, bool sameDrive) =>
        !(move && sameDrive) && (anyFolder || fileBytes > BigBytes);
}

/// <summary>Words for a copy that takes a while: how far it is and how long is left.</summary>
public static class CopyProgress
{
    /// <summary>"About 2 minutes left", or null until there is enough to go on.</summary>
    public static string? TimeLeft(long done, long total, TimeSpan elapsed)
    {
        if (done <= 0 || total <= done || elapsed < TimeSpan.FromSeconds(2))
            return null;
        var seconds = (total - done) / (done / elapsed.TotalSeconds);
        return seconds switch
        {
            < 10 => "A few seconds left",
            < 60 => "Less than a minute left",
            < 90 => "About a minute left",
            < 3600 => $"About {Math.Round(seconds / 60)} minutes left",
            _ => $"About {Math.Round(seconds / 3600, 1).ToString(CultureInfo.CurrentCulture)} hours left",
        };
    }
}
