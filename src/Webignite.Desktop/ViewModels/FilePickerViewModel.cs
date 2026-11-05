using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>What a pick is for: files to open, a folder, or where to save one.</summary>
public enum PickKind
{
    Files,
    Folder,
    Save,
}

/// <summary>
/// The app's own Open and Save box: the explorer from Files, with your client folders, favourites and tags,
/// and a bar at the bottom to pick. Used in place of Windows' Open and Save boxes everywhere in the app.
/// </summary>
public sealed partial class FilePickerViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices services;
    private readonly IReadOnlyList<string> patterns;
    private string? replacing;

    public FilePickerViewModel(AppServices services, PickKind kind, string title, IReadOnlyList<string>? patterns = null,
        bool many = false, string? kindName = null, string? suggestedName = null, string? start = null)
    {
        this.services = services;
        this.patterns = patterns ?? [];
        Kind = kind;
        Title = title;
        Many = many;
        Shows = FilePicking.Describe(kindName, this.patterns);
        FileName = suggestedName ?? "";
        var from = start is { Length: > 0 } && Directory.Exists(start) ? start
            : services.Settings.Files.LastPicked is { Length: > 0 } last && Directory.Exists(last) ? last : null;
        Files = new FilesViewModel(services, from)
        {
            PickFilter = kind == PickKind.Folder ? _ => false : name => FilePicking.Fits(name, this.patterns),
            PickFile = PickedByOpening,
        };
        Files.PickedChanged += PickedChanged;
        Files.Refresh();
    }

    public FilesViewModel Files { get; }
    public PickKind Kind { get; }
    public string Title { get; }
    public bool Many { get; }

    /// <summary>"Pictures (*.png, *.jpg)", or "" when any file will do.</summary>
    public string Shows { get; }

    public bool IsSave => Kind == PickKind.Save;
    public bool HasShows => Shows.Length > 0 && Kind != PickKind.Folder;

    public string ChooseText => Kind switch
    {
        PickKind.Save => "Save",
        PickKind.Folder => "Use this folder",
        _ => "Open",
    };

    [ObservableProperty] public partial string FileName { get; set; } = "";
    [ObservableProperty] public partial string Message { get; private set; } = "";

    /// <summary>Settles when the box closes: the paths picked, or null when it was closed without picking.</summary>
    public TaskCompletionSource<IReadOnlyList<string>?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Asks the window to close once a pick is made.</summary>
    public Action? Close { get; set; }

    partial void OnFileNameChanged(string value) => replacing = null;

    // Saving: picking a file puts its name in the box, to save over it or start from it.
    private void PickedChanged()
    {
        if (Kind == PickKind.Save && Files.Selected is [{ IsFolder: false } one])
            FileName = one.Name;
        Message = "";
    }

    // Double-click or Enter on a file: that file is the pick (with the others picked alongside it, when several may be).
    private void PickedByOpening(FileRowViewModel row)
    {
        if (Kind == PickKind.Save)
        {
            FileName = row.Name;
            Choose();
            return;
        }
        var others = Many && Files.Selected.Contains(row) ? Files.Selected.Where(r => !r.IsFolder).Select(r => r.Entry.Path) : [];
        Finish([.. others.DefaultIfEmpty(row.Entry.Path)]);
    }

    private bool InRealFolder => Files.Folder.Length > 0 && Directory.Exists(Files.Folder);

    [RelayCommand]
    private void Choose()
    {
        switch (Kind)
        {
            case PickKind.Files:
                var files = Files.Selected.Where(r => !r.IsFolder).Select(r => r.Entry.Path).ToList();
                if (files.Count == 0)
                {
                    if (Files.Selected is [{ IsFolder: true } folder])
                        Files.Open(folder);
                    else
                        Message = Many ? "Pick one or more files, or double-click one." : "Pick a file, or double-click it.";
                    return;
                }
                Finish(Many ? files : [files[0]]);
                break;
            case PickKind.Folder:
                if (Files.Selected is [{ IsFolder: true } picked])
                {
                    Finish([picked.Entry.Path]);
                    return;
                }
                if (!InRealFolder)
                {
                    Message = "Open a folder first.";
                    return;
                }
                Finish([Files.Folder]);
                break;
            case PickKind.Save:
                if (!InRealFolder)
                {
                    Message = "Open the folder to save it in first.";
                    return;
                }
                if (FilePicking.SaveName(FileName, patterns) is not { } name)
                {
                    Message = "Type a name for the file.";
                    return;
                }
                var path = Path.Combine(Files.Folder, name);
                if (File.Exists(path) && !string.Equals(replacing, path, StringComparison.OrdinalIgnoreCase))
                {
                    replacing = path;
                    Message = $"There's a file called {name} here already. Press Save again to replace it.";
                    return;
                }
                Finish([path]);
                break;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Done.TrySetResult(null);
        Close?.Invoke();
    }

    private void Finish(List<string> paths)
    {
        var folder = Kind == PickKind.Folder ? paths[0] : Path.GetDirectoryName(paths[0]);
        if (folder is { Length: > 0 })
            services.Settings.Files = services.Settings.Files with { LastPicked = folder };
        Done.TrySetResult(paths);
        Close?.Invoke();
    }

    public void Dispose()
    {
        Done.TrySetResult(null);
        Files.PickedChanged -= PickedChanged;
        Files.Dispose();
    }
}
