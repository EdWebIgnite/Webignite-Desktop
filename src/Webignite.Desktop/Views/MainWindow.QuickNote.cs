using Webignite.Core.Notes;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The quick note: a shortcut from any program opens a small box, and what is typed goes into Notes. Pressing
/// the shortcut again while the box is open brings it to the front. The shortcut is Windows only; the box
/// itself works anywhere.
/// </summary>
public partial class MainWindow
{
    private QuickNoteWindow? quickNote;

    private void WireQuickNote(AppServices services)
    {
        services.Go.QuickNoteAction = () => ShowQuickNote(services);
        OnShortcut(GlobalShortcut.QuickNote, () => services.Go.QuickNote());
        // The box closes (and keeps what was typed) before the app does.
        Closing += (_, _) => quickNote?.Close();
    }

    private void ShowQuickNote(AppServices services)
    {
        if (quickNote is { } open)
        {
            open.Activate();
            return;
        }
        var box = new QuickNoteWindow(services);
        box.Closed += (_, _) =>
        {
            quickNote = null;
            if (box.Saved is { } saved)
                ShowNote("Quick note saved", $"{NoteRules.Name(saved)} is in Notes. Click to open it.", () =>
                {
                    services.Go.Note(saved.Id);
                    Activate();
                });
            if (box.SavedTodo is { } todo)
            {
                (DataContext as MainViewModel)?.RefreshPlanning();
                var due = todo.Due is { } day ? ", due " + Webignite.Core.Wording.Day(day, DateOnly.FromDateTime(DateTime.Today)) : "";
                ShowNote("To-do added", $"{todo.Title}{due}. Click to see your to-dos.", () =>
                {
                    services.Go.MyTasks();
                    Activate();
                });
            }
            if (box.SavedEvent is { } added)
            {
                (DataContext as MainViewModel)?.RefreshPlanning();
                ShowNote("Event added", $"{added.Title} is in the calendar. Click to see it.", () =>
                {
                    services.Go.To(Page.Calendar);
                    Activate();
                });
            }
        };
        quickNote = box;
        box.Show();
    }
}
