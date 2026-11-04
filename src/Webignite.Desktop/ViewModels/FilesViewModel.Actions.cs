using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// What Files does to files: new folders, renaming, the Recycle Bin, copy, cut and paste, bringing files in by
/// dragging, and Ctrl+Z to put the last change back. Each change is recorded before the next.
/// </summary>
public sealed partial class FilesViewModel
{
    private readonly Stack<FileChange> changes = new();

    public bool CanUndo => changes.Count > 0;
    public string UndoText => changes.TryPeek(out var last) ? "Undo: " + last.What : "Undo";

    private void Record(FileChange change)
    {
        changes.Push(change);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoText));
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Ctrl+Shift+N: a new folder here, ready to be named.</summary>
    [RelayCommand]
    private void NewFolder()
    {
        if (NotInTagView())
            return;
        string? made = null;
        Try(() => made = FileOps.NewFolder(Folder), "Couldn't make the folder");
        if (made is null)
            return;
        Record(new FileChange.Made([made]));
        Load(keepSelection: false);
        if (Items.OfType<FileRowViewModel>().FirstOrDefault(r => string.Equals(r.Entry.Path, made, StringComparison.OrdinalIgnoreCase)) is { } row)
        {
            Selected = [row];
            Restored?.Invoke(Selected);
            StartRename(row);
        }
    }

    /// <summary>F2: the name becomes a box to type in. Enter keeps it, Esc puts it back.</summary>
    public void StartRename(FileRowViewModel? row = null)
    {
        row ??= Selected.FirstOrDefault();
        if (row is null)
            return;
        foreach (var other in Items.OfType<FileRowViewModel>().Where(r => r.IsRenaming && r != row))
            other.IsRenaming = false;
        row.RenameText = row.Name;
        row.RenameProblem = "";
        row.IsRenaming = true;
        RenameStarted?.Invoke(row);
    }

    /// <summary>Set by the view: puts the cursor in the name box once it shows.</summary>
    public Action<FileRowViewModel>? RenameStarted { get; set; }

    [RelayCommand]
    private void Rename() => StartRename();

    /// <summary>Keeps the typed name. Returns false while the name can't be used, with why under the box.</summary>
    public bool FinishRename(FileRowViewModel row)
    {
        if (!row.IsRenaming)
            return true;
        if (row.RenameText.Trim() == row.Name)
        {
            row.IsRenaming = false;
            return true;
        }
        if (FileNames.Problem(row.RenameText) is { } problem)
        {
            row.RenameProblem = problem;
            return false;
        }
        try
        {
            var to = FileOps.Rename(row.Entry.Path, row.RenameText);
            services.FileTags.Moved(row.Entry.Path, to);
            Record(new FileChange.Renamed(row.Entry.Path, to));
            row.IsRenaming = false;
            Load(keepSelection: false);
            if (Items.OfType<FileRowViewModel>().FirstOrDefault(r => string.Equals(r.Entry.Path, to, StringComparison.OrdinalIgnoreCase)) is { } renamed)
            {
                Selected = [renamed];
                Restored?.Invoke(Selected);
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            row.RenameProblem = e.Message;
            return false;
        }
    }

    public static void CancelRename(FileRowViewModel row) => row.IsRenaming = false;

    /// <summary>Delete: to the Recycle Bin, so it can be got back from there.</summary>
    [RelayCommand]
    private void Delete()
    {
        var paths = SelectedPaths;
        if (paths.Count == 0)
            return;
        Try(() =>
        {
            FileOps.Recycle(paths);
            Say(paths.Count == 1 ? $"{Path.GetFileName(paths[0])} is in the Recycle Bin." : $"{paths.Count} items are in the Recycle Bin.", problem: false);
        }, "Couldn't delete it");
        Load(keepSelection: false);
    }

    /// <summary>Ctrl+C. The view also puts the files on Windows' clipboard, to paste in other programs.</summary>
    [RelayCommand]
    private void Copy()
    {
        if (Selected.Count == 0)
            return;
        services.FileClipboard.Set(SelectedPaths, cut: false);
        MarkCut();
        Say(Selected.Count == 1 ? $"Copied {Selected[0].Name}. Paste it where it should go." : $"Copied {Selected.Count} items.", problem: false);
    }

    /// <summary>Ctrl+X: moved when pasted, shown faded until then.</summary>
    [RelayCommand]
    private void Cut()
    {
        if (Selected.Count == 0)
            return;
        services.FileClipboard.Set(SelectedPaths, cut: true);
        MarkCut();
        Say(Selected.Count == 1 ? $"Cut {Selected[0].Name}. Paste it where it should go." : $"Cut {Selected.Count} items.", problem: false);
    }

    private void MarkCut()
    {
        foreach (var row in Items.OfType<FileRowViewModel>())
            row.IsCut = services.FileClipboard.IsCut(row.Entry.Path);
    }

    /// <summary>Ctrl+V: what was copied or cut in Files goes here; failing that, files the view found on Windows' clipboard.</summary>
    public void Paste(IReadOnlyList<string>? fromWindows = null)
    {
        if (NotInTagView())
            return;
        var clipboard = services.FileClipboard;
        var paths = clipboard.Paths.Count > 0 ? clipboard.Paths : fromWindows ?? [];
        paths = [.. paths.Where(p => File.Exists(p) || Directory.Exists(p))];
        if (paths.Count == 0)
        {
            Say("There's nothing to paste.", problem: false);
            return;
        }
        Bring(paths, move: clipboard.Cut && clipboard.Paths.Count > 0);
        if (clipboard.Cut)
            clipboard.Clear();
    }

    [RelayCommand]
    private void PasteHere() => Paste();

    /// <summary>Files dropped here or pasted: copied, or moved when cut (or dragged with Shift held).</summary>
    public void Bring(IReadOnlyList<string> paths, bool move, string? into = null)
    {
        if (into is null && NotInTagView())
            return;
        into ??= Folder;
        Try(() =>
        {
            if (move)
            {
                var moved = FileOps.Move(paths, into);
                foreach (var (from, to) in moved)
                    services.FileTags.Moved(from, to);
                if (moved.Count > 0)
                    Record(new FileChange.Moved(moved));
                var where = into == Folder ? "here" : "to " + Path.GetFileName(into.TrimEnd('\\', '/'));
                Say(moved.Count == 1 ? $"Moved {Path.GetFileName(moved[0].To)} {where}." : $"Moved {moved.Count} items {where}.", problem: false);
            }
            else
            {
                var made = FileOps.Copy(paths, into);
                for (var i = 0; i < made.Count; i++)
                    services.FileTags.Copied(paths[i], made[i]);
                Record(new FileChange.Made(made));
                var where = into == Folder ? "here" : "to " + Path.GetFileName(into.TrimEnd('\\', '/'));
                Say(made.Count == 1 ? $"Copied {Path.GetFileName(made[0])} {where}." : $"Copied {made.Count} items {where}.", problem: false);
            }
        }, move ? "Couldn't move it" : "Couldn't copy it");
        Load(keepSelection: false);
    }

    /// <summary>Ctrl+Z: puts back the last rename, move, copy or new folder made here.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!changes.TryPop(out var last))
            return;
        Try(() =>
        {
            last.Undo();
            // Tags go back with the file.
            if (last is FileChange.Renamed renamed)
                services.FileTags.Moved(renamed.To, renamed.From);
            else if (last is FileChange.Moved moved)
                foreach (var (from, to) in moved.Items)
                    services.FileTags.Moved(to, from);
            Say("Put back: " + last.What.ToLowerInvariant() + ".", problem: false);
        }, "Couldn't put it back");
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoText));
        UndoCommand.NotifyCanExecuteChanged();
        Load(keepSelection: false);
    }

    /// <summary>The path to put on the clipboard for "Copy path": the picked items', or this folder's.</summary>
    public string PathsToCopy() => Selected.Count == 0 ? Folder : string.Join(Environment.NewLine, SelectedPaths);

    [RelayCommand]
    private void OpenWith()
    {
        if (Selected.FirstOrDefault() is { IsFolder: false } row)
            Try(() =>
            {
                if (OperatingSystem.IsWindows())
                    WindowsShell.OpenWith(row.Entry.Path);
            }, "Couldn't open it");
    }

    [RelayCommand]
    private void ShowInExplorer()
    {
        var path = Selected.FirstOrDefault()?.Entry.Path ?? Folder;
        Try(() =>
        {
            if (OperatingSystem.IsWindows())
                WindowsShell.ShowInExplorer(path);
        }, "Couldn't open Explorer");
    }

    [RelayCommand]
    private void Properties()
    {
        var path = Selected.FirstOrDefault()?.Entry.Path ?? Folder;
        Try(() =>
        {
            if (OperatingSystem.IsWindows())
                WindowsShell.Properties(path);
        }, "Couldn't show its properties");
    }

    [RelayCommand]
    private void OpenPicked()
    {
        if (Selected.Count == 1)
            Open(Selected[0]);
        else
            foreach (var row in Selected.Where(r => !r.IsFolder))
                Try(() => FileOps.Open(row.Entry.Path), "Couldn't open it");
    }

    /// <summary>Ctrl+click or a middle click on a folder: it opens in a Files tab of its own.</summary>
    public void OpenInNewTab(FileRowViewModel row)
    {
        if (row.IsFolder)
            OpenInNewTab(row.Entry.Path);
    }

    public void OpenInNewTab(string folder) => services.Go.Folder(folder, newTab: true);

    [RelayCommand]
    private void OpenPickedInNewTab()
    {
        foreach (var row in Selected.Where(r => r.IsFolder))
            OpenInNewTab(row);
    }

    // Tags

    /// <summary>Right-click, Tags: puts the tag on everything picked, or takes it off when they all have it.</summary>
    [RelayCommand]
    private void ToggleTag(FileTag tag)
    {
        if (Selected.Count == 0)
            return;
        var on = !Selected.All(r => r.Tags.Any(t => t.Id == tag.Id));
        services.FileTags.Set(SelectedPaths, tag.Id, on);
        Say(on ? $"Tagged {Picked()} {tag.Name}." : $"Took {tag.Name} off {Picked()}.", problem: false);
    }

    private string Picked() => Selected.Count == 1 ? Selected[0].Name : $"{Selected.Count} items";

    /// <summary>The Tags button: only files with this tag in the folder, or all again (null).</summary>
    [RelayCommand]
    private void FilterBy(FileTag? tag)
    {
        FilterTag = tag;
        Lay(SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>In a tag's view: opens the folder a file is in, with the file picked.</summary>
    public void ShowInItsFolder(FileRowViewModel row)
    {
        if (FilePaths.Parent(row.Entry.Path) is not { } parent)
            return;
        GoTo(parent);
        if (Items.OfType<FileRowViewModel>().FirstOrDefault(r => string.Equals(r.Entry.Path, row.Entry.Path, StringComparison.OrdinalIgnoreCase)) is { } found)
        {
            Selected = [found];
            Restored?.Invoke(Selected);
            UpdateStatus();
        }
    }

    // Tags changed here, in another Files tab or in Settings: the side list and the rows show it.
    private void TagsChanged(object? sender, EventArgs e)
    {
        Places = BuildPlaces();
        OnPropertyChanged(nameof(Places));
        OnPropertyChanged(nameof(AllTags));
        if (ShowingTag is { } shown)
        {
            if (TagCalled(TagPrefix + shown.Id) is not { } now)
            {
                GoTo(Folder);
                return;
            }
            ShowingTag = now;
        }
        if (FilterTag is { } filter)
            FilterTag = services.FileTags.Tags.FirstOrDefault(t => t.Id == filter.Id);
        Load(keepSelection: true);
    }

    private bool NotInTagView()
    {
        if (!IsTagView)
            return false;
        Say("Open a folder first: a tag's files come from all over.", problem: false);
        return true;
    }
}
