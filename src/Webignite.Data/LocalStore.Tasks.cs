using Webignite.Core.Tasks;

namespace Webignite.Data;

/// <summary>To-dos. The key of an automatic one stays readable, so the same reminder is never raised twice.</summary>
public sealed partial class LocalStore
{
    private const string TaskContext = "task:";

    public TaskItem SaveTask(TaskItem task)
    {
        Run("""
            INSERT INTO tasks (id, auto_key, payload) VALUES ($id, $key, $payload)
            ON CONFLICT (id) DO UPDATE SET payload = $payload, deleted = 0
            """,
            ("$id", task.Id), ("$key", task.AutoKey), ("$payload", Seal(TaskContext + task.Id, task)));
        return task;
    }

    /// <summary>
    /// Removes a task from view. Automatic tasks keep a marker so the same reminder isn't raised again.
    /// </summary>
    public void DeleteTask(string id) => Run("""
        UPDATE tasks SET deleted = 1, payload = x'' WHERE id = $id AND auto_key IS NOT NULL;
        DELETE FROM tasks WHERE id = $id AND auto_key IS NULL;
        """, ("$id", id));

    /// <summary>
    /// Ticks off a document's open follow-up and reminder to-dos: the chasing mail went, or Xero says it's paid,
    /// accepted or declined, so they're done.
    /// </summary>
    public void TickOffChasing(string documentId, DateTimeOffset now)
    {
        foreach (var task in ListTasks().Where(t => t.DocumentId == documentId && AutomaticTasks.IsChase(t)))
            SaveTask(task with { Done = true, DoneAt = now });
    }

    public IReadOnlyList<TaskItem> ListTasks() => ReadAll<TaskItem>(TaskContext, "SELECT id, payload FROM tasks WHERE deleted = 0");

    /// <summary>Every automatic-task key ever raised, including ticked-off and deleted ones.</summary>
    public IReadOnlySet<string> AutoTaskKeys() => ReadKeys("SELECT auto_key FROM tasks WHERE auto_key IS NOT NULL");
}
