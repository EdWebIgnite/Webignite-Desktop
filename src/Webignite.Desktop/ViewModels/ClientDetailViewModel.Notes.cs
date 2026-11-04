using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Notes;

namespace Webignite.Desktop.ViewModels;

// Notes from the Notes page that are about this client: the newest few, and a way to start one.
public partial class ClientDetailViewModel
{
    private const int NotesShown = 6;

    public ObservableCollection<ClientNoteLink> NoteLinks { get; } = [];

    public bool HasNoteLinks => NoteLinks.Count > 0;
    public bool NoNoteLinks => NoteLinks.Count == 0;
    public string MoreNotesText { get; private set; } = "";
    public bool HasMoreNotes => MoreNotesText.Length > 0;

    private void LoadNoteLinks()
    {
        var notes = NoteRules.Ordered(NoteRules.In(services.Notes.List(), NotePlace.Client, clientId: client.Id));
        var now = DateTimeOffset.Now;
        NoteLinks.Clear();
        foreach (var note in notes.Take(NotesShown))
            NoteLinks.Add(new ClientNoteLink(note.Id, NoteRules.Name(note), NotesViewModel.When(note.Changed, now), services));
        MoreNotesText = notes.Count > NotesShown ? $"See all {notes.Count} in Notes" : "";
        OnPropertyChanged(nameof(HasNoteLinks));
        OnPropertyChanged(nameof(NoNoteLinks));
        OnPropertyChanged(nameof(MoreNotesText));
        OnPropertyChanged(nameof(HasMoreNotes));
    }

    /// <summary>A new note about this client, opened in Notes.</summary>
    [RelayCommand]
    private void NewClientNote()
    {
        var note = services.Notes.New(services.Notes.Notebooks()[0].Id, clientId: client.Id);
        services.Go.Note(note.Id);
    }

    [RelayCommand]
    private void AllClientNotes() => services.Go.ClientNotes(client.Id, client.Name);
}

/// <summary>A note about the client, on their page.</summary>
public sealed partial class ClientNoteLink(string id, string name, string when, AppServices services)
{
    public string Name { get; } = name;
    public string When { get; } = when;

    [RelayCommand]
    private void Open() => services.Go.Note(id);
}
