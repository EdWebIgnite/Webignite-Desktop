using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;
using Webignite.Services;

namespace Webignite.Desktop.ViewModels;

// Duplicate files: the files under a folder that are byte for byte the same, in sets, to tick the extra copies
// and send them to the Recycle Bin. Nothing goes without a tick, and one copy of each always stays.
public sealed partial class ToolsViewModel
{
    // Stops the search that is running, when there is one.
    private Action? stopDuplicates;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindDuplicatesCommand))]
    public partial string DuplicateFolder { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindDuplicatesCommand), nameof(StopDuplicatesCommand))]
    public partial bool DuplicatesBusy { get; set; }

    [ObservableProperty] public partial string DuplicateSummary { get; set; } = "";
    [ObservableProperty] public partial bool DuplicateProblem { get; set; }
    [ObservableProperty] public partial string DuplicateProgressText { get; set; } = "";

    public ObservableCollection<DuplicateSetView> DuplicateSets { get; } = [];

    public bool HasDuplicateSets => DuplicateSets.Count > 0;

    public int DuplicatesTicked => DuplicateSets.Sum(s => s.Files.Count(f => f.IsTicked));

    public string RecycleDuplicatesText => DuplicatesTicked switch
    {
        0 => "Tick the copies to remove",
        1 => "Move 1 file to the Recycle Bin",
        var n => $"Move {n:N0} files to the Recycle Bin",
    };

    private bool CanFindDuplicates() => !DuplicatesBusy && DuplicateFolder.Trim().Length > 0;

    /// <summary>Fills in the folder and starts looking. Used by Files' right-click.</summary>
    public void StartDuplicates(string folder)
    {
        DuplicateFolder = folder;
        FindDuplicatesCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanFindDuplicates))]
    private async Task FindDuplicates()
    {
        var folder = DuplicateFolder.Trim().Trim('"');
        if (!Directory.Exists(folder))
        {
            DuplicateSummary = "There is no folder at that address.";
            DuplicateProblem = true;
            return;
        }
        using var source = new CancellationTokenSource();
        stopDuplicates = source.Cancel;
        var stop = source.Token;
        DuplicatesBusy = true;
        DuplicateProblem = false;
        DuplicateSummary = "";
        DuplicateProgressText = "Listing the files…";
        ShowDuplicates([]);
        try
        {
            var progress = new Progress<DuplicateProgress>(p => DuplicateProgressText = p.FilesRead == 0
                ? $"{p.FilesSeen:N0} files found so far…"
                : $"{p.FilesSeen:N0} files; reading the ones that could be copies: {p.FilesRead:N0} read.");
            var sets = await Task.Run(() => FileComparer.Duplicates(folder, progress, stop), stop);
            ShowDuplicates(sets);
            DuplicateSummary = Duplicates.Summary(sets);
        }
        catch (OperationCanceledException)
        {
            DuplicateSummary = "Stopped.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DuplicateSummary = "That folder couldn't be read: " + e.Message;
            DuplicateProblem = true;
        }
        finally
        {
            stopDuplicates = null;
            DuplicatesBusy = false;
            DuplicateProgressText = "";
        }
    }

    private bool CanStopDuplicates() => DuplicatesBusy;

    [RelayCommand(CanExecute = nameof(CanStopDuplicates))]
    private void StopDuplicates() => stopDuplicates?.Invoke();

    /// <summary>Stops a search still running. Called when the window closes.</summary>
    public void LeaveDuplicates()
    {
        stopDuplicates?.Invoke();
    }

    /// <summary>Shows sets of copies. Public for the screenshot tool.</summary>
    public void ShowDuplicates(IReadOnlyList<DuplicateSet> sets)
    {
        DuplicateSets.Clear();
        // A folder full of copies can have thousands of sets; the ones that free the most are first.
        foreach (var set in sets.Take(500))
            DuplicateSets.Add(new DuplicateSetView(set, this));
        DuplicateTicksChanged();
        OnPropertyChanged(nameof(HasDuplicateSets));
    }

    internal void DuplicateTicksChanged()
    {
        OnPropertyChanged(nameof(DuplicatesTicked));
        OnPropertyChanged(nameof(RecycleDuplicatesText));
        RecycleDuplicatesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Ticks every copy but the one nearest the top of the folder in each set.</summary>
    [RelayCommand]
    private void TickExtraCopies()
    {
        foreach (var set in DuplicateSets)
        {
            var extras = Duplicates.Extras(set.Set);
            foreach (var file in set.Files)
                file.IsTicked = extras.Contains(file.Path);
        }
    }

    [RelayCommand]
    private void ClearDuplicateTicks()
    {
        foreach (var file in DuplicateSets.SelectMany(s => s.Files))
            file.IsTicked = false;
    }

    private bool CanRecycleDuplicates() => DuplicatesTicked > 0 && !DuplicatesBusy;

    [RelayCommand(CanExecute = nameof(CanRecycleDuplicates))]
    private async Task RecycleDuplicates()
    {
        // Never every copy of something: when a whole set is ticked, one stays.
        var going = DuplicateSets.SelectMany(s => Duplicates.SafeToRemove(s.Set, [.. s.Files.Where(f => f.IsTicked).Select(f => f.Path)])).ToList();
        var asked = DuplicatesTicked;
        try
        {
            await Task.Run(() => FileOps.Recycle(going));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            DuplicateSummary = "Not all of them could be moved: " + e.Message;
            DuplicateProblem = true;
        }
        var gone = going.Where(p => !File.Exists(p)).ToHashSet();
        var left = DuplicateSets.Select(s => s.Set with { Paths = [.. s.Set.Paths.Where(p => !gone.Contains(p))] }).Where(s => s.Paths.Count > 1).ToList();
        ShowDuplicates(left);
        if (!DuplicateProblem)
            DuplicateSummary = (gone.Count == 1 ? "1 file is in the Recycle Bin" : $"{gone.Count:N0} files are in the Recycle Bin")
                + (gone.Count < asked ? "; one copy of each was kept." : ".") + (left.Count > 0 ? " " + Duplicates.Summary(left) : "");
    }

    internal void ShowDuplicateFile(string path)
    {
        services.Go.ShowFile(path);
        services.Go.MainToFront();
    }
}

public sealed class DuplicateSetView
{
    public DuplicateSetView(DuplicateSet set, ToolsViewModel tools)
    {
        Set = set;
        Files = [.. set.Paths.Select(p => new DuplicateFileView(p, tools))];
        Title = $"{set.Paths.Count} copies, {FileSizes.Format(set.Size)} each";
    }

    public DuplicateSet Set { get; }
    public IReadOnlyList<DuplicateFileView> Files { get; }
    public string Title { get; }
}

public sealed partial class DuplicateFileView(string path, ToolsViewModel tools) : ObservableObject
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";

    [ObservableProperty] public partial bool IsTicked { get; set; }

    partial void OnIsTickedChanged(bool value) => tools.DuplicateTicksChanged();

    [RelayCommand]
    private void Show() => tools.ShowDuplicateFile(Path);
}
