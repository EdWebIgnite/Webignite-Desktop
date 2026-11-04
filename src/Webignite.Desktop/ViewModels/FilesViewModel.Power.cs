using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Files' power tools: the Show filter (kind and date), Space (folder sizes and what's taking the room), renaming
/// many at once, and the copy queue for big copies and moves.
/// </summary>
public sealed partial class FilesViewModel
{
    // Show: by kind and by date

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterLabel), nameof(IsShowFiltered), nameof(EmptyTitle), nameof(EmptyText))]
    public partial KindFilter ShowKind { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterLabel), nameof(IsShowFiltered), nameof(EmptyTitle), nameof(EmptyText))]
    public partial DateFilter ShowDate { get; private set; }

    public string FilterLabel => FileFilters.Label(ShowKind, ShowDate);
    public bool IsShowFiltered => ShowKind != KindFilter.All || ShowDate != DateFilter.Any;

    [RelayCommand]
    private void ShowOnly(KindFilter kind)
    {
        ShowKind = kind;
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void ShowFrom(DateFilter date)
    {
        ShowDate = date;
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void ShowEverything()
    {
        ShowKind = KindFilter.All;
        ShowDate = DateFilter.Any;
        FilterTag = null;
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    // Space: every folder's size with everything in it, biggest first, with a bar for its share.

    private readonly Dictionary<string, long> folderSizes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? counting;
    private int foldersToCount;
    private int foldersCounted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindHeading))]
    public partial bool ShowingSpace { get; set; }

    partial void OnShowingSpaceChanged(bool value)
    {
        if (!loaded)
            return;
        if (value)
        {
            View = FileView.Details;
            CountSizes();
        }
        else
        {
            StopCounting();
        }
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void ToggleSpace() => ShowingSpace = !ShowingSpace;

    private long SizeOf(FileEntry entry) => entry.Size ?? (folderSizes.TryGetValue(entry.Path, out var size) ? size : 0);

    private List<FileEntry> BySpace(IEnumerable<FileEntry> shown) => [.. shown.OrderByDescending(SizeOf).ThenBy(e => e.Name, NaturalOrder.Instance)];

    private double SpacePart(FileEntry entry)
    {
        if (!ShowingSpace)
            return 0;
        var total = entries.Sum(SizeOf);
        return total == 0 ? 0 : (double)SizeOf(entry) / total;
    }

    /// <summary>Adds up the folders shown, off the screen's thread, biggest-looking last; the list re-sorts as each is done.</summary>
    private void CountSizes()
    {
        StopCounting();
        if (!ShowingSpace)
            return;
        var folders = entries.Where(e => e.IsFolder && !folderSizes.ContainsKey(e.Path)).Select(e => e.Path).ToList();
        if (folders.Count == 0)
        {
            UpdateStatus();
            return;
        }
        var stop = new CancellationTokenSource();
        counting = stop;
        foldersToCount = folders.Count;
        foldersCounted = 0;
        UpdateStatus();
        CountSizesAsync(folders, stop).Forget("Adding up folder sizes");
    }

    private async Task CountSizesAsync(IReadOnlyList<string> folders, CancellationTokenSource stop)
    {
        foreach (var folder in folders)
        {
            long size;
            try
            {
                size = await Task.Run(() => FolderBytes(folder, stop.Token), stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (counting != stop)
                return;
            folderSizes[folder] = size;
            foldersCounted++;
            Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        if (counting == stop)
        {
            counting = null;
            UpdateStatus();
        }
    }

    private static long FolderBytes(string folder, CancellationToken stop)
    {
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            stop.ThrowIfCancellationRequested();
            try
            {
                total += file.Length;
            }
            catch (IOException)
            {
                // A file that went away while being counted.
            }
        }
        return total;
    }

    /// <summary>A tab closed while its copy runs on: the copy carries on, but no longer holds on to the tab.</summary>
    private void StopWatchingJobs()
    {
        foreach (var job in watchedJobs)
            job.Finished -= CopyFinished;
        watchedJobs.Clear();
    }

    private void StopCounting()
    {
        counting?.Cancel();
        counting?.Dispose();
        counting = null;
    }

    /// <summary>The status bar's words in Space: how far the adding up is, or the total.</summary>
    private string? SpaceStatus() => !ShowingSpace ? null
        : counting is not null ? $"Adding up folder sizes… {foldersCounted} of {foldersToCount}"
        : $"{FileSizes.Format(entries.Sum(SizeOf))} in all";

    // Renaming many at once

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBulkRenaming))]
    public partial BulkRenameViewModel? BulkRename { get; private set; }

    public bool IsBulkRenaming => BulkRename is not null;

    /// <summary>F2 with several picked, or right-click, Rename: the names to change, shown before and after.</summary>
    public void StartBulkRename()
    {
        var picked = Selected.ToList();
        if (picked.Count < 2)
        {
            StartRename();
            return;
        }
        var folder = Path.GetDirectoryName(picked[0].Entry.Path.TrimEnd('\\', '/')) ?? "";
        if (picked.Any(r => !FileTags.Same(Path.GetDirectoryName(r.Entry.Path.TrimEnd('\\', '/')) ?? "", folder)))
        {
            Say("Pick files from one folder to rename them together.", problem: false);
            return;
        }
        var others = FileOps.List(folder).Select(e => e.Name).ToList();
        BulkRename = new BulkRenameViewModel([.. picked.Select(r => r.Entry.Path)], others, ApplyBulkRename, () => BulkRename = null);
    }

    private void ApplyBulkRename(IReadOnlyList<(string From, string To)> renames)
    {
        try
        {
            FileOps.RenameMany(renames);
            foreach (var (from, to) in renames)
                services.FileTags.Moved(from, to);
            Record(new FileChange.RenamedMany(renames));
            BulkRename = null;
            Say($"Renamed {renames.Count} items.", problem: false);
            Load(keepSelection: false);
            var renamed = renames.Select(r => r.To).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Selected = [.. Items.OfType<FileRowViewModel>().Where(r => renamed.Contains(r.Entry.Path))];
            Restored?.Invoke(Selected);
            PickChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            BulkRename?.Failed(e.Message);
        }
    }

    // The copy queue

    /// <summary>The big copies and moves under way, from any Files tab.</summary>
    public ObservableCollection<CopyJob> CopyJobs => services.CopyQueue.Jobs;

    private static bool ToQueue(IReadOnlyList<string> paths, bool move, string into)
    {
        var anyFolder = paths.Any(Directory.Exists);
        var bytes = paths.Where(File.Exists).Sum(p => new FileInfo(p).Length);
        var sameDrive = paths.All(p => string.Equals(Path.GetPathRoot(p), Path.GetPathRoot(into), StringComparison.OrdinalIgnoreCase));
        return CopyRules.Queue(anyFolder, bytes, move, sameDrive);
    }

    private readonly List<CopyJob> watchedJobs = [];

    private void Queue(IReadOnlyList<string> paths, bool move, string into)
    {
        var job = services.CopyQueue.Add(paths, into, move);
        job.Finished += CopyFinished;
        watchedJobs.Add(job);
        Say(move ? "Moving them; see the bar below." : "Copying them; see the bar below.", problem: false);
    }

    // A queued job finished: Undo can put it back, and the folder shows what arrived.
    private void CopyFinished(object? sender, EventArgs e)
    {
        if (sender is not CopyJob job)
            return;
        job.Finished -= CopyFinished;
        watchedJobs.Remove(job);
        if (disposed)
            return;
        if (job.State == CopyState.Done && job.Pairs.Count > 0)
        {
            Record(job.Move ? new FileChange.Moved(job.Pairs) : new FileChange.Made([.. job.Pairs.Select(p => p.To)]));
            Say((job.Move ? "Moved " : "Copied ") + (job.Pairs.Count == 1 ? Path.GetFileName(job.Pairs[0].To) : $"{job.Pairs.Count} items") + ".", problem: false);
        }
        if (!IsGathered)
            Load(keepSelection: true);
    }
}

/// <summary>Renaming several files at once: find and replace, or a name with numbers, shown before it is done.</summary>
public sealed partial class BulkRenameViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> paths;
    private readonly IReadOnlyList<string> others;
    private readonly Action<IReadOnlyList<(string From, string To)>> apply;
    private readonly Action cancel;

    public BulkRenameViewModel(IReadOnlyList<string> paths, IReadOnlyList<string> others, Action<IReadOnlyList<(string From, string To)>> apply, Action cancel)
    {
        this.paths = paths;
        this.others = others;
        this.apply = apply;
        this.cancel = cancel;
        Plan();
    }

    public string Title => $"Rename {paths.Count} items";

    [ObservableProperty] public partial string Find { get; set; } = "";
    [ObservableProperty] public partial string Replace { get; set; } = "";
    [ObservableProperty] public partial bool MatchCase { get; set; }

    /// <summary>Give them all one name and a number, in the order picked.</summary>
    [ObservableProperty] public partial bool Numbered { get; set; }

    [ObservableProperty] public partial string NumberedName { get; set; } = "";
    [ObservableProperty] public partial decimal StartAt { get; set; } = 1;
    [ObservableProperty] public partial decimal Digits { get; set; } = 2;

    [ObservableProperty] public partial IReadOnlyList<BulkRename.Change> Changes { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool CanApply { get; private set; }

    [ObservableProperty] public partial string Problem { get; private set; } = "";

    partial void OnFindChanged(string value) => Plan();
    partial void OnReplaceChanged(string value) => Plan();
    partial void OnMatchCaseChanged(bool value) => Plan();
    partial void OnNumberedChanged(bool value) => Plan();
    partial void OnNumberedNameChanged(string value) => Plan();
    partial void OnStartAtChanged(decimal value) => Plan();
    partial void OnDigitsChanged(decimal value) => Plan();

    private void Plan()
    {
        var options = new BulkRename.Options
        {
            Find = Find,
            Replace = Replace,
            MatchCase = MatchCase,
            NumberedName = Numbered ? NumberedName : null,
            StartAt = (int)Math.Clamp(StartAt, 0, 100_000),
            Digits = (int)Math.Clamp(Digits, 1, 6),
        };
        Changes = Webignite.Core.Files.BulkRename.Plan([.. paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/')))], options, others);
        CanApply = Webignite.Core.Files.BulkRename.CanApply(Changes);
        Problem = Changes.FirstOrDefault(c => c.Problem is not null)?.Problem ?? "";
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        var renames = paths.Zip(Changes)
            .Where(p => p.Second.Changes)
            .Select(p => (p.First, Path.Combine(Path.GetDirectoryName(p.First.TrimEnd('\\', '/'))!, p.Second.To)))
            .ToList();
        apply(renames);
    }

    [RelayCommand]
    private void Cancel() => cancel();

    internal void Failed(string why) => Problem = "Nothing was renamed: " + why;
}
