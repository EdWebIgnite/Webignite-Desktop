using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Notes;

namespace Webignite.Desktop.ViewModels;

// Notes from the Notes page that are about the open project: the newest few on its Overview, and a way to start one.
public partial class ProjectsViewModel
{
    private const int ProjectNotesShown = 6;

    public ObservableCollection<ClientNoteLink> ProjectNotes { get; } = [];

    public bool HasProjectNotes => ProjectNotes.Count > 0;
    public bool NoProjectNotes => ProjectNotes.Count == 0;
    public string MoreProjectNotesText { get; private set; } = "";
    public bool HasMoreProjectNotes => MoreProjectNotesText.Length > 0;

    private void LoadProjectNotes()
    {
        ProjectNotes.Clear();
        MoreProjectNotesText = "";
        if (open is not null)
        {
            var notes = NoteRules.Ordered(NoteRules.In(services.Notes.List(), NotePlace.Project, projectId: open.Id));
            var now = DateTimeOffset.Now;
            foreach (var note in notes.Take(ProjectNotesShown))
                ProjectNotes.Add(new ClientNoteLink(note.Id, NoteRules.Name(note), NotesViewModel.When(note.Changed, now), services));
            MoreProjectNotesText = notes.Count > ProjectNotesShown ? $"See all {notes.Count} in Notes" : "";
        }
        OnPropertyChanged(nameof(HasProjectNotes));
        OnPropertyChanged(nameof(NoProjectNotes));
        OnPropertyChanged(nameof(MoreProjectNotesText));
        OnPropertyChanged(nameof(HasMoreProjectNotes));
    }

    /// <summary>A new note about this project (and its client), opened in Notes.</summary>
    [RelayCommand]
    private void NewProjectNote()
    {
        if (open is null)
            return;
        var note = services.Notes.New(services.Notes.Notebooks()[0].Id, clientId: open.ClientId, projectId: open.Id);
        services.Go.Note(note.Id);
    }

    [RelayCommand]
    private void AllProjectNotes()
    {
        if (open is not null)
            services.Go.ProjectNotes(open.Id, open.Name);
    }
}
