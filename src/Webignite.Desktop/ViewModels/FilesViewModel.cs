using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Files: an explorer for every folder on the PC, with the client folders (e.g. C:\Projects) at the top of its
/// side list. It can be open in several tabs or windows at once, each on its own folder. A folder is watched
/// while it is shown, and thumbnails are drawn only for what is on screen; both stop when the page is let go.
/// The changes it makes are in FilesViewModel.Actions.cs.
/// </summary>
public sealed partial class FilesViewModel : ViewModelBase, IRefreshable, IDisposable, ITabTitle
{
    private readonly AppServices services;
    private readonly Stack<string> back = new();
    private readonly Stack<string> forward = new();
    private readonly DispatcherTimer reloadSoon = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private FileSystemWatcher? watcher;
    private IReadOnlyList<FileEntry> entries = [];
    private bool loaded;

    public FilesViewModel(AppServices services, string? start = null)
    {
        this.services = services;
        var settings = services.Settings.Files;
        View = settings.View;
        Sort = settings.Sort;
        Descending = settings.Descending;
        Grouping = settings.Grouping;
        ThumbnailSize = settings.ThumbnailSize;
        reloadSoon.Tick += (_, _) =>
        {
            reloadSoon.Stop();
            Load(keepSelection: true);
        };
        Places = BuildPlaces();
        loaded = true;
        GoTo(start ?? DefaultStart());
    }

    /// <summary>The rows and, when grouped, their headings, in order.</summary>
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>The side list: client folders, the usual folders, and the drives.</summary>
    public IReadOnlyList<FilePlaceGroup> Places { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Crumbs), nameof(TabTitle), nameof(CanGoUp))]
    public partial string Folder { get; private set; } = "";

    [ObservableProperty] public partial string AddressText { get; set; } = "";
    [ObservableProperty] public partial bool EditingAddress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThumbnails), nameof(IsList), nameof(IsDetails))]
    public partial FileView View { get; set; }

    [ObservableProperty] public partial FileSort Sort { get; set; }
    [ObservableProperty] public partial bool Descending { get; set; }
    [ObservableProperty] public partial FileGrouping Grouping { get; set; }
    [ObservableProperty] public partial int ThumbnailSize { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = "";

    [ObservableProperty] public partial bool MessageIsProblem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewing))]
    public partial AttachmentPreviewViewModel? Preview { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "";

    public bool IsThumbnails => View == FileView.Thumbnails;
    public bool IsList => View == FileView.List;
    public bool IsDetails => View == FileView.Details;
    public bool HasMessage => Message.Length > 0;
    public bool IsPreviewing => Preview is not null;
    public bool CanGoUp => FilePaths.Parent(Folder) is not null;
    public bool IsEmpty => entries.Count == 0;

    public IReadOnlyList<FileCrumb> Crumbs => [.. FilePaths.Crumbs(Folder).Select(c => new FileCrumb(c.Name, c.Path, this))];

    public string? TabTitle => Folder.Length == 0 ? null : FilePaths.Crumbs(Folder).LastOrDefault().Name;

    /// <summary>The rows picked in the view, kept in step by the view.</summary>
    public IReadOnlyList<FileRowViewModel> Selected { get; set; } = [];

    public IReadOnlyList<string> SelectedPaths => [.. Selected.Select(r => r.Entry.Path)];

    /// <summary>The client folder (e.g. C:\Projects) when it is set or there; otherwise Documents.</summary>
    private string DefaultStart()
    {
        if (ClientRoot() is { } root)
            return root;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length > 0 && Directory.Exists(documents) ? documents : Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    }

    /// <summary>Where client folders live: the one set in Settings, else C:\Projects when it exists.</summary>
    public string? ClientRoot() => FileOps.ClientRoot(services.Settings.Files);

    private List<FilePlaceGroup> BuildPlaces()
    {
        var groups = new List<FilePlaceGroup>();
        if (ClientRoot() is { } root)
        {
            var folders = FileOps.Folders(root);
            var clients = services.Clients.List()
                .Select(c => (Client: c, Match: ClientFolders.For(c, folders)))
                .Where(c => c.Match.Fit == ClientFolders.Fit.Sure && c.Match.Folder is not null)
                .Select(c => new FilePlace(c.Client.Name, c.Match.Folder!, "Clients", this))
                .ToList();
            groups.Add(new FilePlaceGroup("CLIENTS", [new FilePlace("All client folders", root, "Folder", this), .. clients]));
        }
        groups.Add(new FilePlaceGroup("FOLDERS", [.. FileOps.UsualFolders().Select(f => new FilePlace(f.Name, f.Path, "Folder", this))]));
        groups.Add(new FilePlaceGroup("THIS PC", [.. FileOps.Drives().Select(d => new FilePlace(d.Name, d.Path, "Domains", this))]));
        return groups;
    }

    // Moving about

    /// <summary>Shows a folder, adding the one before to Back.</summary>
    public void GoTo(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0)
            return;
        if (!Directory.Exists(path))
        {
            Say($"There's no folder at {path}.", problem: true);
            return;
        }
        if (Folder.Length > 0 && !string.Equals(Folder, path, StringComparison.OrdinalIgnoreCase))
        {
            back.Push(Folder);
            forward.Clear();
        }
        Show(path);
    }

    private void Show(string path)
    {
        Folder = path;
        AddressText = path;
        EditingAddress = false;
        ClosePreview();
        Say("", problem: false);
        Load(keepSelection: false);
        Watch();
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoBack() => back.Count > 0;
    private bool CanGoForward() => forward.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (back.Count == 0)
            return;
        forward.Push(Folder);
        Show(back.Pop());
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward()
    {
        if (forward.Count == 0)
            return;
        back.Push(Folder);
        Show(forward.Pop());
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void Up()
    {
        if (FilePaths.Parent(Folder) is { } parent)
            GoTo(parent);
    }

    /// <summary>The address bar: a path typed and Enter pressed.</summary>
    [RelayCommand]
    private void GoToAddress() => GoTo(AddressText);

    [RelayCommand]
    private void EditAddress()
    {
        AddressText = Folder;
        EditingAddress = true;
    }

    [RelayCommand]
    private void StopEditingAddress()
    {
        AddressText = Folder;
        EditingAddress = false;
    }

    /// <summary>Double-click or Enter: a folder opens here, a PDF or picture in the preview, anything else in its program.</summary>
    public void Open(FileRowViewModel row)
    {
        if (row.Entry.IsFolder)
        {
            GoTo(row.Entry.Path);
            return;
        }
        if (FileKinds.CanPreview(row.Name))
        {
            ShowPreview(row.Entry.Path);
            return;
        }
        Try(() => FileOps.Open(row.Entry.Path), "Couldn't open it");
    }

    public void ShowPreview(string path)
    {
        ClosePreview();
        var preview = new AttachmentPreviewViewModel(Path.GetFileName(path), () => Try(() => FileOps.Open(path), "Couldn't open it"), null, ClosePreview);
        Preview = preview;
        LoadPreviewAsync(preview, path).Forget("Showing the file");
    }

    private static async Task LoadPreviewAsync(AttachmentPreviewViewModel preview, string path)
    {
        var data = await File.ReadAllBytesAsync(path);
        await preview.Load(data, FileKinds.IsPdf(path) ? Webignite.Core.Mail.PreviewKind.Pdf : Webignite.Core.Mail.PreviewKind.Picture);
    }

    [RelayCommand]
    private void ClosePreview()
    {
        Preview?.Free();
        Preview = null;
    }

    public void Refresh()
    {
        if (!loaded)
            return;
        Places = BuildPlaces();
        OnPropertyChanged(nameof(Places));
        Load(keepSelection: true);
    }

    // The list

    /// <summary>Reads the folder again and lays out the rows, sorted and grouped. Pictures' thumbnails are let go.</summary>
    private void Load(bool keepSelection)
    {
        var picked = keepSelection ? SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            entries = FileOps.List(Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            entries = [];
            Say($"Couldn't read {Folder}: {e.Message}", problem: true);
        }
        Lay(picked);
    }

    private void Lay(HashSet<string> picked)
    {
        FreeRows();
        Items.Clear();
        var sorted = FileLists.Sort(entries, Sort, Descending);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = new List<FileRowViewModel>();
        foreach (var (heading, group) in FileLists.Group(sorted, Grouping, today))
        {
            if (heading.Length > 0)
                Items.Add(new FileHeaderRow(heading, group.Count));
            foreach (var entry in group)
            {
                var row = new FileRowViewModel(entry, this) { IsCut = services.FileClipboard.IsCut(entry.Path) };
                rows.Add(row);
                Items.Add(row);
            }
        }
        Selected = [.. rows.Where(r => picked.Contains(r.Entry.Path))];
        Restored?.Invoke(Selected);
        OnPropertyChanged(nameof(IsEmpty));
        UpdateStatus();
    }

    /// <summary>Set by the view: picks rows again after the list was laid out afresh.</summary>
    public Action<IReadOnlyList<FileRowViewModel>>? Restored { get; set; }

    public void UpdateStatus()
    {
        var count = entries.Count;
        var items = count == 1 ? "1 item" : $"{count} items";
        StatusText = Selected.Count switch
        {
            0 => items,
            1 when Selected[0].Entry.Size is { } size => $"{items}, 1 picked ({FileSizes.Format(size)})",
            var n => $"{items}, {n} picked" + (Selected.All(r => r.Entry.Size is not null) ? $" ({FileSizes.Format(Selected.Sum(r => r.Entry.Size ?? 0))})" : ""),
        };
    }

    partial void OnViewChanged(FileView value) => KeepLook();
    partial void OnThumbnailSizeChanged(int value) => KeepLook();

    partial void OnSortChanged(FileSort value)
    {
        KeepLook();
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    partial void OnDescendingChanged(bool value)
    {
        KeepLook();
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    partial void OnGroupingChanged(FileGrouping value)
    {
        KeepLook();
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    // How folders are shown is the same in every tab, and kept for next time.
    private void KeepLook()
    {
        if (loaded)
            services.Settings.Files = services.Settings.Files with { View = View, Sort = Sort, Descending = Descending, Grouping = Grouping, ThumbnailSize = ThumbnailSize };
    }

    [RelayCommand]
    private void ShowAs(FileView view) => View = view;

    /// <summary>A column heading clicked: sorts by it, or the other way round when it already is.</summary>
    [RelayCommand]
    private void SortBy(FileSort by)
    {
        if (Sort == by)
        {
            Descending = !Descending;
            return;
        }
        Sort = by;
        Descending = by is FileSort.Modified or FileSort.Size;
    }

    [RelayCommand]
    private void GroupBy(FileGrouping by) => Grouping = by;

    // Watching

    // While a folder is on screen, changes made elsewhere (a download landing, a save from Word) show by themselves.
    private void Watch()
    {
        StopWatching();
        try
        {
            watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += Changed;
            watcher.Changed += Changed;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            StopWatching();
        }
    }

    private void Changed(object sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        reloadSoon.Stop();
        reloadSoon.Start();
    });

    private void StopWatching()
    {
        if (watcher is null)
            return;
        watcher.EnableRaisingEvents = false;
        watcher.Created -= Changed;
        watcher.Deleted -= Changed;
        watcher.Renamed -= Changed;
        watcher.Changed -= Changed;
        watcher.Dispose();
        watcher = null;
    }

    private void FreeRows()
    {
        foreach (var row in Items.OfType<FileRowViewModel>())
            row.Free();
    }

    /// <summary>Closed or let go: the folder stops being watched and every thumbnail and preview is let go.</summary>
    public void Dispose()
    {
        StopWatching();
        reloadSoon.Stop();
        FreeRows();
        Items.Clear();
        ClosePreview();
        GC.SuppressFinalize(this);
    }

    private void Say(string message, bool problem)
    {
        Message = message;
        MessageIsProblem = problem;
    }

    private void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Say($"{what}: {e.Message}", problem: true);
        }
    }
}

/// <summary>One file or folder in the list.</summary>
public sealed partial class FileRowViewModel(FileEntry entry, FilesViewModel owner) : ViewModelBase
{
    private bool loading;
    private bool freed;

    public FileEntry Entry => entry;
    public FilesViewModel Owner => owner;
    public string Name => entry.Name;
    public string Kind => entry.Kind;
    public string SizeText => FileSizes.Format(entry.Size);
    public string ModifiedText => entry.Modified.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    public bool IsFolder => entry.IsFolder;
    public bool IsPicture => !entry.IsFolder && FileKinds.IsPicture(entry.Name);

    /// <summary>The line icon for the row, from Styles/Icons.axaml.</summary>
    public string Icon => entry.IsFolder ? "Folder" : FileKinds.IsPicture(entry.Name) ? "Picture" : "Quotes";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial Bitmap? Thumbnail { get; private set; }

    public bool HasThumbnail => Thumbnail is not null;

    [ObservableProperty] public partial bool IsCut { get; set; }
    [ObservableProperty] public partial bool IsRenaming { get; set; }
    [ObservableProperty] public partial string RenameText { get; set; } = "";
    [ObservableProperty] public partial string RenameProblem { get; set; } = "";

    /// <summary>Called when the row comes on screen in the thumbnails view: draws a small copy of the picture, off the screen's thread.</summary>
    public void LoadThumbnail(int width)
    {
        if (!IsPicture || loading || Thumbnail is not null || freed)
            return;
        loading = true;
        Draw(width).Forget("Drawing a thumbnail");
    }

    private async Task Draw(int width)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(entry.Path);
                return Bitmap.DecodeToWidth(stream, Math.Max(width, 32), BitmapInterpolationMode.MediumQuality);
            });
            if (freed)
                bitmap.Dispose();
            else
                Thumbnail = bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // A picture that can't be read just shows its icon.
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>Lets go of the thumbnail, once nothing is drawing it.</summary>
    public void Free()
    {
        freed = true;
        var thumbnail = Thumbnail;
        Thumbnail = null;
        if (thumbnail is not null)
            Dispatcher.UIThread.Post(thumbnail.Dispose, DispatcherPriority.Background);
    }
}

/// <summary>A group's heading in the list, when grouped by kind or date.</summary>
public sealed record FileHeaderRow(string Heading, int Count)
{
    public string CountText => Count == 1 ? "1 item" : $"{Count} items";
}

/// <summary>One folder on the way, in the address bar.</summary>
public sealed partial class FileCrumb(string name, string path, FilesViewModel owner) : ViewModelBase
{
    public string Name => name;
    public string Path => path;

    [RelayCommand]
    private void Go() => owner.GoTo(path);
}

public sealed record FilePlaceGroup(string Title, IReadOnlyList<FilePlace> Places);

/// <summary>A place in the side list: a client's folder, a usual folder or a drive.</summary>
public sealed partial class FilePlace(string name, string path, string icon, FilesViewModel owner) : ViewModelBase
{
    public string Name => name;
    public string Path => path;
    public string Icon => icon;

    [RelayCommand]
    private void Go() => owner.GoTo(path);
}
