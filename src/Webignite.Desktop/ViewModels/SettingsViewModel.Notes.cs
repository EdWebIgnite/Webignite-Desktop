using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Notes;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Settings, Notes: the quick-note shortcut that works from any program, and the notebook quick notes go into.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial string QuickNoteShortcut { get; private set; } = "";
    [ObservableProperty] public partial string QuickNoteMessage { get; private set; } = "";
    [ObservableProperty] public partial bool QuickNoteProblem { get; private set; }
    [ObservableProperty] public partial Notebook? QuickNotebook { get; private set; }

    public ObservableCollection<Notebook> Notebooks { get; } = [];

    private void LoadNotes()
    {
        QuickNoteShortcut = services.Settings.Notes.QuickShortcut is { Length: > 0 } keys ? keys : "Off";
        Notebooks.Clear();
        foreach (var notebook in services.Notes.Notebooks())
            Notebooks.Add(notebook);
        QuickNotebook = Notebooks.FirstOrDefault(n => n.Id == services.Settings.Notes.QuickNotebookId) ?? Notebooks.FirstOrDefault();
    }

    /// <summary>The keys pressed in the quick-note shortcut box.</summary>
    public void SetQuickNoteShortcut(string keys)
    {
        var (hotkey, problem) = Hotkey.Parse(keys);
        if (hotkey is null)
        {
            QuickNoteSay(problem!, true);
            return;
        }
        if (Clash(hotkey, GlobalShortcut.QuickNote) is { } clash)
        {
            QuickNoteSay(clash, true);
            return;
        }
        services.Settings.Notes = services.Settings.Notes with { QuickShortcut = hotkey.ToString() };
        QuickNoteShortcut = hotkey.ToString();
        var taken = services.ApplyQuickNoteShortcut?.Invoke();
        QuickNoteSay(taken ?? $"Press {hotkey} in any program to jot down a quick note.", taken is not null);
    }

    [RelayCommand]
    private void QuickNoteOff()
    {
        services.Settings.Notes = services.Settings.Notes with { QuickShortcut = "" };
        QuickNoteShortcut = "Off";
        services.ApplyQuickNoteShortcut?.Invoke();
        QuickNoteSay("The shortcut is off. Write notes on the Notes page.", false);
    }

    [RelayCommand]
    private void QuickNoteBack() => SetQuickNoteShortcut(NoteSettings.DefaultShortcut);

    /// <summary>The notebook picked for quick notes; the list is one-way, so only a real pick counts.</summary>
    public void PickQuickNotebook(Notebook notebook)
    {
        services.Settings.Notes = services.Settings.Notes with { QuickNotebookId = notebook.Id };
        QuickNotebook = notebook;
    }

    [RelayCommand]
    private void OpenQuickNote() => services.Go.QuickNote();

    private void QuickNoteSay(string message, bool problem)
    {
        QuickNoteMessage = message;
        QuickNoteProblem = problem;
    }
}
