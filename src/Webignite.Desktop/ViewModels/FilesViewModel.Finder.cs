using System.Collections.ObjectModel;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The parts of Files taken from Apple's Finder: search as you type (names, and words inside files when asked),
/// Quick Look on Space, the details pane, column view, Favourites and Recents, and quick actions on right-click.
/// </summary>
public sealed partial class FilesViewModel
{
    // Search

    private readonly DispatcherTimer searchSoon = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? searching;
    private Dictionary<string, FileHit>? searchHits;
    private bool searchRunning;
    private int searchLooked;

    /// <summary>What is typed in the search box. A folder is searched with everything below it; a tag or Recents by name.</summary>
    [ObservableProperty] public partial string SearchText { get; set; } = "";

    /// <summary>Also look for the words inside PDFs, Word documents and text files. Slower, so it is a choice.</summary>
    [ObservableProperty] public partial bool SearchInside { get; set; }

    public bool IsSearching => SearchText.Trim().Length > 0;

    partial void OnSearchTextChanged(string value)
    {
        if (!loaded)
            return;
        searchSoon.Stop();
        if (!IsSearching)
        {
            StopSearch(clear: false);
            GatheredChanged();
            Load(keepSelection: true);
            return;
        }
        searchSoon.Start();
    }

    partial void OnSearchInsideChanged(bool value)
    {
        if (loaded && IsSearching)
            StartSearch();
    }

    private void SearchTick(object? sender, EventArgs e)
    {
        searchSoon.Stop();
        StartSearch();
    }

    /// <summary>Starts the search for what is typed now, stopping the one before.</summary>
    public void StartSearch()
    {
        StopSearch(clear: false);
        GatheredChanged();
        if (!IsSearching)
            return;
        if (IsTagView || ShowingRecents)
        {
            Load(keepSelection: true); // filtered by name as it is laid out
            return;
        }
        var stop = new CancellationTokenSource();
        searching = stop;
        searchHits = new Dictionary<string, FileHit>(StringComparer.OrdinalIgnoreCase);
        searchRunning = true;
        searchLooked = 0;
        Load(keepSelection: false);
        RunSearch(Folder, SearchText, SearchInside, stop).Forget("Searching for files");
    }

    private async Task RunSearch(string root, string query, bool inside, CancellationTokenSource stop)
    {
        try
        {
            await FileSearcher.Run(root, query, inside, hits =>
            {
                if (searching != stop || searchHits is null)
                    return;
                foreach (var hit in hits)
                    searchHits[hit.Entry.Path] = hit;
                Load(keepSelection: true);
            }, looked =>
            {
                if (searching != stop)
                    return;
                searchLooked = looked;
                UpdateStatus();
            }, stop.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        // The last batch is posted to the screen's thread; finish after it.
        Dispatcher.UIThread.Post(() =>
        {
            if (searching != stop)
                return;
            searchRunning = false;
            OnPropertyChanged(nameof(EmptyTitle));
            UpdateStatus();
        }, DispatcherPriority.Background);
    }

    private string SearchStatus(int count)
    {
        var where = FilePaths.Crumbs(Folder).LastOrDefault().Name ?? Folder;
        var found = count == 1 ? "1 found" : $"{count} found";
        if (searchRunning)
            return $"Searching {where} and below… {found}, {searchLooked:N0} looked at";
        return count >= FileSearch.MostResults ? $"The first {count} found in {where} and below. Type more to narrow it." : $"{found} in {where} and below";
    }

    /// <summary>Stops a search that is running; with <paramref name="clear"/> the box is emptied too.</summary>
    private void StopSearch(bool clear)
    {
        searching?.Cancel();
        searching?.Dispose();
        searching = null;
        searchHits = null;
        searchRunning = false;
        if (clear && SearchText.Length > 0)
        {
            loaded = false; // emptying the box mustn't start anything
            SearchText = "";
            loaded = true;
        }
    }

    /// <summary>Esc in the search box: back to the folder as it was.</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        StopSearch(clear: true);
        GatheredChanged();
        Load(keepSelection: true);
    }

    // Recents and Favourites

    private void Remember(string path)
    {
        services.Settings.FileRecents = FileRecents.Add(services.Settings.FileRecents, path);
        if (ShowingRecents)
            Load(keepSelection: true);
    }

    public bool IsFavourite(string folder) => services.Settings.Files.Favourites.Any(f => FileTags.Same(f, folder));

    /// <summary>Pins a folder to Favourites in the side list.</summary>
    public void AddFavourite(string folder)
    {
        if (IsFavourite(folder))
            return;
        services.Settings.Files = services.Settings.Files with { Favourites = [.. services.Settings.Files.Favourites, folder] };
        PlacesChanged();
        Say($"{Path.GetFileName(folder.TrimEnd('\\', '/'))} is in Favourites.", problem: false);
    }

    public void RemoveFavourite(string folder)
    {
        services.Settings.Files = services.Settings.Files with { Favourites = [.. services.Settings.Files.Favourites.Where(f => !FileTags.Same(f, folder))] };
        PlacesChanged();
    }

    private void PlacesChanged()
    {
        Places = BuildPlaces();
        OnPropertyChanged(nameof(Places));
    }

    // The picked items: status, the details pane and Quick Look follow the pick.

    private bool detailsPending;

    /// <summary>Called by the view when the pick changes.</summary>
    public void PickChanged()
    {
        UpdateStatus();
        // After the keys waiting now, so holding an arrow key doesn't draw every file on the way.
        if (ShowDetails && !detailsPending)
        {
            detailsPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                detailsPending = false;
                UpdateDetails();
            }, DispatcherPriority.Background);
        }
        if (QuickLook is not null)
        {
            if (Selected.Count == 1)
                OpenQuickLook(Selected[0]);
            else if (Selected.Count == 0)
                CloseQuickLook();
        }
    }

    // Details pane

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    public partial FileLookViewModel? Details { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPickSummary))]
    public partial FilePickSummary? PickSummary { get; private set; }

    public bool HasDetails => Details is not null;
    public bool HasPickSummary => PickSummary is not null;

    [ObservableProperty] public partial bool ShowDetails { get; set; }

    partial void OnShowDetailsChanged(bool value)
    {
        if (!loaded)
            return;
        services.Settings.Files = services.Settings.Files with { ShowDetails = value };
        if (value)
        {
            UpdateDetails();
            return;
        }
        Details?.Free();
        Details = null;
        PickSummary = null;
    }

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    private void UpdateDetails()
    {
        if (!ShowDetails)
            return;
        var row = Selected.Count == 1 ? Selected[0] : null;
        if (row is not null && Details?.Path == row.Entry.Path)
            return;
        Details?.Free();
        Details = null;
        PickSummary = Selected.Count > 1
            ? new FilePickSummary(Selected.Count, Selected.All(r => r.Entry.Size is not null) ? Selected.Sum(r => r.Entry.Size ?? 0) : null)
            : null;
        if (row is null)
            return;
        var look = new FileLookViewModel(row.Entry, big: false, row.Tags, row.Snippet);
        Details = look;
        look.Load().Forget("Showing the file's details");
    }

    // Quick Look: Space shows a big preview over the list; Space or Esc closes it, arrows go through the folder.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQuickLooking))]
    public partial FileLookViewModel? QuickLook { get; private set; }

    public bool IsQuickLooking => QuickLook is not null;

    public void ToggleQuickLook()
    {
        if (QuickLook is not null)
            CloseQuickLook();
        else if (Selected.FirstOrDefault() is { } row)
            OpenQuickLook(row);
    }

    private void OpenQuickLook(FileRowViewModel row)
    {
        if (QuickLook?.Path == row.Entry.Path)
            return;
        QuickLook?.Free();
        var look = new FileLookViewModel(row.Entry, big: true, row.Tags, row.Snippet);
        QuickLook = look;
        look.Load().Forget("Showing the file");
    }

    [RelayCommand]
    private void CloseQuickLook()
    {
        QuickLook?.Free();
        QuickLook = null;
    }

    [RelayCommand]
    private void OpenLooked()
    {
        if (QuickLook is { } look && Items.OfType<FileRowViewModel>().FirstOrDefault(r => r.Entry.Path == look.Path) is { } row)
        {
            CloseQuickLook();
            Open(row);
        }
    }

    /// <summary>Set by the view when the list is too narrow for every column: Kind and Tags step aside.</summary>
    [ObservableProperty] public partial bool IsNarrow { get; set; }

    // Column view: the folders on the way to this one, each in a column, with this folder's list last.

    public ObservableCollection<FileColumnViewModel> Columns { get; } = [];

    /// <summary>Columns scroll sideways; the other views wrap or trim to the width on screen.</summary>
    public ScrollBarVisibility ColumnsScroll => IsColumns ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

    private void BuildColumns()
    {
        if (!IsColumns || IsGathered)
        {
            Columns.Clear();
            return;
        }
        var chain = FileColumns.Chain(Folder, ClientRoot());
        Columns.Clear();
        for (var i = 0; i < chain.Count - 1; i++)
            Columns.Add(new FileColumnViewModel(chain[i], chain[i + 1], this));
    }

    /// <summary>Left arrow in column view: back to the folder before, with this one picked.</summary>
    public void UpAndPick()
    {
        var from = Folder;
        if (FilePaths.Parent(Folder) is not { } parent)
            return;
        GoTo(parent);
        if (Items.OfType<FileRowViewModel>().FirstOrDefault(r => FileTags.Same(r.Entry.Path, from)) is { } row)
        {
            Selected = [row];
            Restored?.Invoke(Selected);
            PickChanged();
        }
    }

    // Quick actions

    public IReadOnlyList<string> PickedPictures => [.. Selected.Where(r => !r.IsFolder && QuickActions.CanRotate(r.Name)).Select(r => r.Entry.Path)];

    public bool CanCombine => QuickActions.CanCombine([.. Selected.Select(r => r.Name)]);

    [RelayCommand]
    private Task RotateLeft() => Rotate(-90);

    [RelayCommand]
    private Task RotateRight() => Rotate(90);

    private async Task Rotate(int degrees)
    {
        var pictures = PickedPictures;
        if (pictures.Count == 0)
            return;
        await Run("Couldn't turn it", async () =>
        {
            await QuickActionRunner.Rotate(pictures, degrees);
            Record(new FileChange.Rotated(pictures, degrees));
            Say(pictures.Count == 1 ? $"Turned {Path.GetFileName(pictures[0])}." : $"Turned {pictures.Count} pictures.", problem: false);
            Load(keepSelection: true);
        });
    }

    [RelayCommand]
    private Task Shrink()
    {
        var pictures = PickedPictures;
        return pictures.Count == 0 ? Task.CompletedTask : Made("Couldn't make a smaller copy", () => QuickActionRunner.Shrink(pictures),
            made => made.Count == 1 ? $"Made {Path.GetFileName(made[0])}, sized for a website." : $"Made {made.Count} copies sized for a website.");
    }

    [RelayCommand]
    private Task Combine()
    {
        var paths = SelectedPaths;
        if (!CanCombine || NotInTagView())
            return Task.CompletedTask;
        var into = Folder;
        return Made("Couldn't make the PDF", async () => (IReadOnlyList<string>)[await QuickActionRunner.Combine(paths, into)],
            made => $"Put {paths.Count} files into {Path.GetFileName(made[0])}.", rename: true);
    }

    [RelayCommand]
    private Task Zip()
    {
        var paths = SelectedPaths;
        if (paths.Count == 0 || NotInTagView())
            return Task.CompletedTask;
        var into = Folder;
        return Made("Couldn't zip it", async () => (IReadOnlyList<string>)[await QuickActionRunner.Zip(paths, into)],
            made => $"Zipped into {Path.GetFileName(made[0])}.");
    }

    // A quick action that makes files: recorded so Undo takes them away, then the first is picked (and named, for a new PDF).
    private Task Made(string problem, Func<Task<IReadOnlyList<string>>> work, Func<IReadOnlyList<string>, string> done, bool rename = false) =>
        Run(problem, async () =>
        {
            var made = await work();
            Record(new FileChange.Made(made));
            Say(done(made), problem: false);
            Load(keepSelection: false);
            if (Items.OfType<FileRowViewModel>().FirstOrDefault(r => FileTags.Same(r.Entry.Path, made[0])) is { } row)
            {
                Selected = [row];
                Restored?.Invoke(Selected);
                PickChanged();
                if (rename)
                    StartRename(row);
            }
        });

    private async Task Run(string problem, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or DllNotFoundException)
        {
            Say($"{problem}: {e.Message}", problem: true);
        }
    }
}

/// <summary>One folder on the way, as a column in column view, with the next folder on the way picked.</summary>
public sealed partial class FileColumnViewModel : ViewModelBase
{
    public FileColumnViewModel(string folder, string next, FilesViewModel owner)
    {
        Folder = folder;
        IReadOnlyList<FileEntry> entries;
        try
        {
            entries = FileLists.Sort(FileOps.List(folder), FileSort.Name, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            entries = [];
        }
        Rows = [.. entries.Select(e => new FileColumnRow(e, FileTags.Same(e.Path, next), owner))];
    }

    public string Folder { get; }
    public IReadOnlyList<FileColumnRow> Rows { get; }
}

public sealed partial class FileColumnRow(FileEntry entry, bool onPath, FilesViewModel owner) : ViewModelBase
{
    public string Name => entry.Name;
    public bool IsFolder => entry.IsFolder;
    public bool IsOnPath => onPath;
    public string Icon => entry.IsFolder ? "Folder" : FileKinds.IsPicture(entry.Name) ? "Picture" : "Quotes";

    /// <summary>A folder opens to the right of it; a file opens its folder with the file picked.</summary>
    [RelayCommand]
    private void Go()
    {
        if (entry.IsFolder)
            owner.GoTo(entry.Path);
        else
            owner.ShowInItsFolder(entry.Path);
    }
}
