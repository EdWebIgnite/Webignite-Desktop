using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Services;

namespace Webignite.Desktop.ViewModels;

// Compare: two files side by side, line by line, or two folders file by file. Read-only.
public sealed partial class ToolsViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCompareCommand))]
    public partial string CompareLeft { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCompareCommand))]
    public partial string CompareRight { get; set; } = "";

    [ObservableProperty] public partial bool CompareIgnoreSpaces { get; set; }
    [ObservableProperty] public partial bool CompareOnlyChanges { get; set; }
    [ObservableProperty] public partial string CompareSummary { get; set; } = "";
    [ObservableProperty] public partial bool CompareProblem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCompareCommand))]
    public partial bool CompareBusy { get; set; }

    /// <summary>Wide enough for the longest line on both sides, so the two halves scroll sideways together.</summary>
    [ObservableProperty] public partial double CompareWidth { get; set; }

    public ObservableCollection<DiffRowView> CompareRows { get; } = [];
    public ObservableCollection<FolderRowView> CompareFolderRows { get; } = [];

    public bool HasCompareRows => CompareRows.Count > 0;
    public bool HasCompareFolderRows => CompareFolderRows.Count > 0;

    private IReadOnlyList<DiffRow> compared = [];
    private (string Left, string Right)? comparedFolders;

    /// <summary>True while two files from a folder comparison are shown, so there is a way back to the folders.</summary>
    [ObservableProperty] public partial bool CanGoBackToFolders { get; set; }

    private bool CanRunCompare() => !CompareBusy && CompareLeft.Trim().Length > 0 && CompareRight.Trim().Length > 0;

    /// <summary>Fills in both sides and compares them. Used by Files' right-click.</summary>
    public void StartCompare(string left, string right)
    {
        (CompareLeft, CompareRight) = (left, right);
        comparedFolders = null;
        CanGoBackToFolders = false;
        RunCompareCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRunCompare))]
    private async Task RunCompare()
    {
        var left = CompareLeft.Trim().Trim('"');
        var right = CompareRight.Trim().Trim('"');
        var (leftIsFolder, rightIsFolder) = (Directory.Exists(left), Directory.Exists(right));
        if ((!leftIsFolder && !File.Exists(left)) || (!rightIsFolder && !File.Exists(right)))
        {
            CompareSaid(!leftIsFolder && !File.Exists(left) ? "There is nothing at the left address." : "There is nothing at the right address.", problem: true);
            return;
        }
        if (leftIsFolder != rightIsFolder)
        {
            CompareSaid("One is a file and the other a folder. Pick two files, or two folders.", problem: true);
            return;
        }
        CompareBusy = true;
        CompareSaid(leftIsFolder ? "Reading the two folders…" : "Reading the two files…", problem: false);
        var ignoreSpaces = CompareIgnoreSpaces;
        try
        {
            if (leftIsFolder)
            {
                var (rows, summary) = await Task.Run(() => FileComparer.Folders(left, right));
                comparedFolders = (left, right);
                CanGoBackToFolders = false;
                compared = [];
                ShowCompareRows();
                CompareFolderRows.Clear();
                // What differs first; a folder of thousands that match is not what was asked about.
                foreach (var row in (rows ?? []).OrderBy(r => r.Kind == FolderDiff.Same).Take(5000))
                    CompareFolderRows.Add(new FolderRowView(row, this));
                CompareSaid(summary, problem: rows is null);
            }
            else
            {
                var result = await Task.Run(() => FileComparer.Files(left, right, ignoreSpaces));
                compared = result.Rows ?? [];
                CompareFolderRows.Clear();
                ShowCompareRows();
                CompareSaid(result.Summary, problem: false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CompareSaid("That couldn't be read: " + e.Message, problem: true);
        }
        finally
        {
            CompareBusy = false;
            OnPropertyChanged(nameof(HasCompareFolderRows));
        }
    }

    partial void OnCompareOnlyChangesChanged(bool value) => ShowCompareRows();

    partial void OnCompareIgnoreSpacesChanged(bool value)
    {
        if (HasCompareRows && RunCompareCommand.CanExecute(null))
            RunCompareCommand.Execute(null);
    }

    private void ShowCompareRows()
    {
        CompareRows.Clear();
        var rows = CompareOnlyChanges ? TextCompare.OnlyChanges(compared) : compared;
        var longest = 0;
        foreach (var row in rows)
        {
            CompareRows.Add(new DiffRowView(row));
            longest = Math.Max(longest, Math.Max(row.Left?.Length ?? 0, row.Right?.Length ?? 0));
        }
        // About 7.3 pixels a letter in the fixed-width type, twice over, plus the two number columns.
        CompareWidth = Math.Min(12000, 2 * (56 + Math.Min(longest, 800) * 7.3));
        OnPropertyChanged(nameof(HasCompareRows));
    }

    private void CompareSaid(string words, bool problem)
    {
        CompareSummary = words;
        CompareProblem = problem;
    }

    [RelayCommand]
    private void SwapCompare() => (CompareLeft, CompareRight) = (CompareRight, CompareLeft);

    /// <summary>From a folder comparison: the two versions of one file, side by side.</summary>
    internal void CompareFiles(FolderRow row)
    {
        if (comparedFolders is not { } folders)
            return;
        var back = folders;
        (CompareLeft, CompareRight) = (Path.Combine(folders.Left, row.Path), Path.Combine(folders.Right, row.Path));
        RunCompareCommand.Execute(null);
        comparedFolders = back;
        CanGoBackToFolders = true;
    }

    [RelayCommand]
    private void BackToFolders()
    {
        if (comparedFolders is not { } folders)
            return;
        StartCompare(folders.Left, folders.Right);
    }
}

/// <summary>A row of two files side by side, worded for the screen.</summary>
public sealed class DiffRowView(DiffRow row)
{
    public string LeftNumber => row.LeftNumber?.ToString() ?? "";
    public string RightNumber => row.RightNumber?.ToString() ?? "";
    public string Left => row.Kind == DiffKind.Skipped ? "" : Tabs(row.Left);
    public string Right => Tabs(row.Right);
    public string SkippedWords => row.Kind == DiffKind.Skipped ? row.Left ?? "" : "";
    public bool IsSkipped => row.Kind == DiffKind.Skipped;
    public bool IsLine => row.Kind != DiffKind.Skipped;

    /// <summary>The left side has something the right doesn't, or had it differently.</summary>
    public bool LeftDiffers => row.Kind is DiffKind.Removed or DiffKind.Changed;
    public bool RightDiffers => row.Kind is DiffKind.Added or DiffKind.Changed;
    public string LeftMark => row.Kind switch { DiffKind.Removed => "−", DiffKind.Changed => "~", _ => "" };
    public string RightMark => row.Kind switch { DiffKind.Added => "+", DiffKind.Changed => "~", _ => "" };

    // A tab is drawn as four spaces, so indented code lines up.
    private static string Tabs(string? text) => (text ?? "").Replace("\t", "    ", StringComparison.Ordinal);
}

/// <summary>A file in two folders being compared.</summary>
public sealed partial class FolderRowView(FolderRow row, ToolsViewModel tools)
{
    public string Path => row.Path;
    public string Words => FolderCompare.Words(row.Kind);
    public bool Differs => row.Kind != FolderDiff.Same;
    public bool CanCompare => row.Kind == FolderDiff.Different;
    public string LeftSize => FileSizes.Format(row.Left?.Size);
    public string RightSize => FileSizes.Format(row.Right?.Size);

    [RelayCommand]
    private void Compare() => tools.CompareFiles(row);
}
