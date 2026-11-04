using Webignite.Core.Notes;

namespace Webignite.Data;

/// <summary>
/// Notebooks and notes. A note's writing is its own row, read only when the note is opened, so the list stays
/// quick however many pictures the notes hold.
/// </summary>
public sealed partial class LocalStore
{
    private const string NotebookContext = "notebook:";
    private const string NoteContext = "note:";
    private const string NoteBodyContext = "notebody:";

    public Notebook SaveNotebook(Notebook notebook) => Put("notebooks", NotebookContext, notebook.Id, notebook);

    public void DeleteNotebook(string id) => Remove("notebooks", id);

    public IReadOnlyList<Notebook> ListNotebooks() => ReadAll<Notebook>(NotebookContext, "SELECT id, payload FROM notebooks");

    public IReadOnlyList<Note> ListNotes() => ReadAll<Note>(NoteContext, "SELECT id, payload FROM notes");

    public Note? FindNote(string id) => Find<Note>("notes", NoteContext, id);

    public Note SaveNote(Note note) => Put("notes", NoteContext, note.Id, note);

    /// <summary>The note and its writing (as HTML), together.</summary>
    public Note SaveNote(Note note, string html) => InOneGo(() =>
    {
        Put("note_bodies", NoteBodyContext, note.Id, new NoteBodyText(html));
        return Put("notes", NoteContext, note.Id, note);
    });

    /// <summary>A note's writing as HTML, or "" when it has none yet.</summary>
    public string NoteBody(string id) => Find<NoteBodyText>("note_bodies", NoteBodyContext, id)?.Html ?? "";

    /// <summary>Several notes changed as one, such as a folder renamed with its notes.</summary>
    public void SaveNotes(Notebook? notebook, IEnumerable<Note> notes) => InOneGo(() =>
    {
        if (notebook is not null)
            Put("notebooks", NotebookContext, notebook.Id, notebook);
        foreach (var note in notes)
            Put("notes", NoteContext, note.Id, note);
        return 0;
    });

    public void DeleteNote(string id) => InOneGo(() =>
    {
        Remove("notes", id);
        Remove("note_bodies", id);
        return 0;
    });
}

/// <summary>A note's writing as it is kept.</summary>
public sealed record NoteBodyText(string Html);
