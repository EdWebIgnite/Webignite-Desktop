using System.Globalization;
using Webignite.Core.Tasks;
using Webignite.Core.Xero;

namespace Webignite.Data;

/// <summary>
/// What Claude and ChatGPT can do with Eddie's to-do list: read it, add to it, change a to-do and tick it off (or
/// open it again). They never delete one. Every change leaves a note so the open app shows it.
/// </summary>
public sealed class AssistantTasks(LocalStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public const int MaxTitle = 200;

    public sealed record Listed(string Id, string Title, string? Notes, string? Due, string? Customer, bool Done, string From, bool Overdue);

    public sealed record Added(string Id, string Title, string? Due, string? Customer, bool AlreadyThere, string Message);

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public IReadOnlyList<Listed> List(bool includeDone = false, string? customer = null, int limit = 50)
    {
        var today = Today;
        return store.ListTasks()
            .Where(t => includeDone || !t.Done)
            .Where(t => string.IsNullOrWhiteSpace(customer) || (t.Customer ?? "").Contains(customer.Trim(), StringComparison.OrdinalIgnoreCase)
                || t.Title.Contains(customer.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Done)
            .ThenBy(t => t.Due ?? DateOnly.MaxValue)
            .ThenByDescending(t => t.Created)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(t => new Listed(t.Id, t.Title, t.Notes, t.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t.Customer, t.Done,
                t.Source switch { TaskSource.Claude => "Claude", TaskSource.ChatGpt => "ChatGPT", TaskSource.Auto => "Webignite", _ => "Eddie" },
                !t.Done && t.Due is { } due && due < today))
            .ToList();
    }

    /// <summary>
    /// Adds a to-do. When an open one with the same title already exists, that one is returned instead, so a
    /// repeated call doesn't fill the list with copies.
    /// </summary>
    public Added Add(string title, TaskSource source, string? due = null, string? customer = null, string? notes = null)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0)
            throw new ArgumentException("Say what needs doing: title is empty.");
        if (title.Length > MaxTitle)
            throw new ArgumentException($"Keep the title under {MaxTitle} characters and put the detail in notes.");
        if (source is not (TaskSource.Claude or TaskSource.ChatGpt))
            throw new ArgumentException("Only assistants add to-dos this way.");

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(due))
        {
            if (!DateOnly.TryParseExact(due.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new ArgumentException("Give due as a date like 2026-11-14.");
            if (parsed < Today.AddDays(-1))
                throw new ArgumentException("That due date has already passed. Leave due out, or use today or later.");
            dueDate = parsed;
        }

        var existing = store.ListTasks().FirstOrDefault(t => !t.Done && string.Equals(t.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return new Added(existing.Id, existing.Title, existing.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), existing.Customer,
                true, "That to-do is already on Eddie's list, so it wasn't added again.");

        var client = string.IsNullOrWhiteSpace(customer) ? null
            : Webignite.Core.Clients.ClientDirectory.Find(store.ListClients(), customer);
        var task = Saved(new TaskItem
        {
            Id = TaskItem.NewId(),
            Title = title,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Due = dueDate,
            Customer = client?.Name ?? (string.IsNullOrWhiteSpace(customer) ? null : customer.Trim()),
            Source = source,
            Created = clock.GetUtcNow(),
        });
        return new Added(task.Id, task.Title, task.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), task.Customer, false,
            "Added to Eddie's to-do list in Webignite, marked as from you.");
    }

    /// <summary>Changes a to-do, found by id or title, and ticks it off or opens it again. What's left out stays as it is.</summary>
    public Listed Update(string todo, string? title = null, string? due = null, string? notes = null, bool? done = null)
    {
        var typed = (todo ?? "").Trim();
        var all = store.ListTasks();
        var task = all.FirstOrDefault(t => t.Id == typed);
        if (task is null)
        {
            var (found, problem) = Webignite.Core.Assistants.AssistantLookup.Find(all.OrderBy(t => t.Done), typed, t => t.Id, t => t.Title, "to-do");
            task = found ?? throw new ArgumentException(problem);
        }
        var (dueGiven, dueDate, dueProblem) = Webignite.Core.Assistants.AssistantLookup.Date(due, "due");
        if (dueProblem.Length > 0)
            throw new ArgumentException(dueProblem);
        var newTitle = string.IsNullOrWhiteSpace(title) ? task.Title : title.Trim();
        if (newTitle.Length > MaxTitle)
            throw new ArgumentException($"Keep the title under {MaxTitle} characters and put the detail in notes.");
        var nowDone = done ?? task.Done;
        var saved = Saved(task with
        {
            Title = newTitle,
            Due = dueGiven ? dueDate : task.Due,
            Notes = notes is null ? task.Notes : string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Done = nowDone,
            DoneAt = nowDone == task.Done ? task.DoneAt : nowDone ? clock.GetUtcNow() : null,
        });
        return List(includeDone: true, limit: 200).FirstOrDefault(t => t.Id == saved.Id)
            ?? new Listed(saved.Id, saved.Title, saved.Notes, saved.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), saved.Customer,
                saved.Done, "Eddie", false);
    }

    private TaskItem Saved(TaskItem task)
    {
        store.SaveTask(task);
        AssistantSignal.Touch(store, "todos", clock);
        return task;
    }
}
