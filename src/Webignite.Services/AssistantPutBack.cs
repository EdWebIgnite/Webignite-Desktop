using Webignite.Core.Projects;
using Webignite.Core.Writing;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Puts back what Claude or ChatGPT replaced, from the list of what they changed: a description, a note's writing,
/// notes on a client, person, event or renewal, or a client's email or name. Each goes through the same service the
/// app uses, applied to the record as stored now, so only that one field changes.
/// </summary>
public sealed class AssistantPutBack(LocalStore store, ProjectService projects, ClientService clients, ContactService contacts, NoteService notes)
{
    /// <summary>Puts it back; a problem in plain words when it can't, else null.</summary>
    public string? Apply(AssistantLog.Entry entry)
    {
        if (!entry.CanPutBack)
            return "That has been put back already.";
        var before = entry.Before ?? "";
        var problem = entry.Field switch
        {
            "task-notes" => projects.ChangeTask(entry.RecordId, t => t with { Notes = before, NotesHtml = entry.BeforeHtml ?? "" }).Saved is null ? "That task has gone." : null,
            "project-description" => projects.Change(entry.RecordId, p => p with { Notes = before, NotesHtml = entry.BeforeHtml ?? "" }).Saved is null ? "That project has gone." : null,
            "note" => PutNoteBack(entry),
            "client-notes" => clients.Change(entry.RecordId, c => c with { Notes = before }) is null ? "That client has gone." : null,
            "client-email" => clients.Change(entry.RecordId, c => c with { Email = before.Length == 0 ? null : before, Check = null }) is null ? "That client has gone." : null,
            "client-name" => clients.Change(entry.RecordId, c => c with { Name = before, Check = null }) is null ? "That client has gone." : null,
            "contact-notes" => contacts.Get(entry.RecordId) is { } person
                ? contacts.Save(person with { Notes = before }).Saved is null ? "That person couldn't be saved." : null
                : "That person has gone.",
            "event-notes" => store.GetEvent(entry.RecordId) is { } calendarEvent ? Saved(store.SaveEvent(calendarEvent with { Notes = before })) : "That event has gone.",
            "renewal-notes" => store.ListRenewals().FirstOrDefault(r => r.Id == entry.RecordId) is { } renewal ? Saved(store.SaveRenewal(renewal with { Notes = before })) : "That renewal has gone.",
            _ => "That can't be put back.",
        };
        if (problem is null)
            AssistantLog.MarkPutBack(store, entry.Id);
        return problem;
    }

    private string? PutNoteBack(AssistantLog.Entry entry)
    {
        if (notes.Get(entry.RecordId) is not { } note)
            return "That note has gone.";
        var writing = entry.BeforeHtml is { Length: > 0 } html ? RichHtml.Read(html, RichHtml.DataAddress) : RichText.FromText(entry.Before);
        return notes.Save(note.Id, note.Title, writing) is null ? "That note has gone." : null;
    }

    private static string? Saved<T>(T _) => null;
}
