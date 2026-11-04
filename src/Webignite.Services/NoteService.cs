using Webignite.Core.Notes;
using Webignite.Core.Writing;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Notebooks and notes, for the Notes page, the client page and the quick note. A change is made to the note
/// as it is stored now, so pinning a note in one window doesn't undo typing saved from another.
/// <see cref="Changed"/> tells every open page, with the note's id, or null when several things changed.
/// </summary>
public sealed class NoteService(LocalStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public event Action<string?>? Changed;

    /// <summary>The notebooks, oldest first. There is always at least one.</summary>
    public IReadOnlyList<Notebook> Notebooks()
    {
        var notebooks = store.ListNotebooks();
        if (notebooks.Count == 0)
            notebooks = [store.SaveNotebook(new Notebook { Id = NewId(), Name = NoteRules.DefaultNotebook, Created = clock.GetUtcNow() })];
        return [.. notebooks.OrderBy(n => n.Created).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Every note, Deleted included. Notes deleted long enough ago go for good here.</summary>
    public IReadOnlyList<Note> List()
    {
        var now = clock.GetUtcNow();
        var notes = store.ListNotes();
        foreach (var expired in notes.Where(n => NoteRules.Expired(n, now)))
            store.DeleteNote(expired.Id);
        return [.. notes.Where(n => !NoteRules.Expired(n, now))];
    }

    public Note? Get(string id) => store.FindNote(id);

    /// <summary>A note's writing, ready for the editor.</summary>
    public RichDocument Writing(string id)
    {
        var html = store.NoteBody(id);
        return html.Length == 0 ? RichDocument.Empty() : RichHtml.Read(html, RichHtml.DataAddress);
    }

    /// <summary>A new, empty note.</summary>
    public Note New(string notebookId, string folder = "", string? clientId = null)
    {
        var now = clock.GetUtcNow();
        var note = store.SaveNote(new Note { Id = NewId(), NotebookId = notebookId, Folder = folder, ClientId = clientId, Created = now, Changed = now }, "");
        Changed?.Invoke(note.Id);
        return note;
    }

    /// <summary>Saves what was typed: the title and the writing. Null when the note was deleted for good meanwhile.</summary>
    public Note? Save(string id, string title, RichDocument writing)
    {
        if (store.FindNote(id) is not { } current)
            return null;
        var note = NoteRules.Describe(current with { Title = title.Trim(), Changed = clock.GetUtcNow() }, writing);
        store.SaveNote(note, RichHtml.Write(writing, RichHtml.ToDataAddress));
        Changed?.Invoke(id);
        return note;
    }

    /// <summary>Changes a note as stored now: pin it, move it, link a client. Null when it's gone.</summary>
    public Note? Change(string id, Func<Note, Note> change)
    {
        if (store.FindNote(id) is not { } current)
            return null;
        var note = store.SaveNote(change(current) with { Id = current.Id });
        Changed?.Invoke(id);
        return note;
    }

    /// <summary>Into Deleted, where it waits <see cref="NoteRules.KeepDeletedDays"/> days.</summary>
    public Note? Delete(string id) => Change(id, n => n with { Deleted = clock.GetUtcNow(), Pinned = false });

    public Note? Restore(string id) => Change(id, n => n with { Deleted = null });

    /// <summary>Gone for good, from Deleted.</summary>
    public void Purge(string id)
    {
        store.DeleteNote(id);
        Changed?.Invoke(id);
    }

    public void EmptyDeleted()
    {
        foreach (var note in store.ListNotes().Where(n => n.Deleted is not null))
            store.DeleteNote(note.Id);
        Changed?.Invoke(null);
    }

    /// <summary>A quick note, typed from any program, saved straight away.</summary>
    public Note Quick(string typed, string? notebookId)
    {
        var notebooks = Notebooks();
        var into = notebooks.FirstOrDefault(n => n.Id == notebookId) ?? notebooks[0];
        var (note, writing) = NoteRules.Quick(typed, NewId(), into.Id, clock.GetUtcNow());
        store.SaveNote(note, RichHtml.Write(writing, RichHtml.ToDataAddress));
        Changed?.Invoke(note.Id);
        return note;
    }

    public Notebook AddNotebook()
    {
        var notebook = store.SaveNotebook(new Notebook { Id = NewId(), Name = NoteRules.NextNotebookName(store.ListNotebooks()), Created = clock.GetUtcNow() });
        Changed?.Invoke(null);
        return notebook;
    }

    public Notebook? RenameNotebook(string id, string typed)
    {
        var name = typed.Trim();
        if (name.Length == 0 || store.ListNotebooks().FirstOrDefault(n => n.Id == id) is not { } notebook)
            return null;
        var saved = store.SaveNotebook(notebook with { Name = name });
        Changed?.Invoke(null);
        return saved;
    }

    /// <summary>Removes an empty notebook; what is in its Deleted goes too. False when it still has notes.</summary>
    public bool RemoveNotebook(string id)
    {
        var notebooks = store.ListNotebooks();
        var notes = store.ListNotes();
        if (notebooks.FirstOrDefault(n => n.Id == id) is not { } notebook || !NoteRules.CanRemove(notebook, notes, notebooks.Count))
            return false;
        foreach (var gone in notes.Where(n => n.NotebookId == id))
            store.DeleteNote(gone.Id);
        store.DeleteNotebook(id);
        Changed?.Invoke(null);
        return true;
    }

    /// <summary>A folder in a notebook. Null when the name is empty or taken.</summary>
    public string? AddFolder(string notebookId, string typed)
    {
        if (store.ListNotebooks().FirstOrDefault(n => n.Id == notebookId) is not { } notebook || NoteRules.NewFolder(notebook, typed) is not { } name)
            return null;
        store.SaveNotebook(notebook with { Folders = [.. notebook.Folders, name] });
        Changed?.Invoke(null);
        return name;
    }

    public void RenameFolder(string notebookId, string folder, string typed)
    {
        if (store.ListNotebooks().FirstOrDefault(n => n.Id == notebookId) is not { } notebook)
            return;
        var (renamed, moved) = NoteRules.RenameFolder(notebook, store.ListNotes(), folder, typed);
        store.SaveNotes(renamed, moved);
        Changed?.Invoke(null);
    }

    public void RemoveFolder(string notebookId, string folder)
    {
        if (store.ListNotebooks().FirstOrDefault(n => n.Id == notebookId) is not { } notebook)
            return;
        var (kept, moved) = NoteRules.RemoveFolder(notebook, store.ListNotes(), folder);
        store.SaveNotes(kept, moved);
        Changed?.Invoke(null);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
