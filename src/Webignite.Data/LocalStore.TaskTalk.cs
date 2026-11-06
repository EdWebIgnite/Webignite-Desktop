using Webignite.Core.Projects;

namespace Webignite.Data;

/// <summary>
/// Comments on tasks and what happened to them, for a task's feed. The task's id is in the clear to find them by;
/// everything else is encrypted. Comments are stamped to sync later; activity is written once and never changed.
/// </summary>
public sealed partial class LocalStore
{
    private const string CommentContext = "task-comment:";
    private const string ActivityContext = "task-activity:";

    public TaskComment SaveComment(TaskComment comment)
    {
        Run("INSERT INTO task_comments (id, task_id, payload) VALUES ($id, $task, $payload) ON CONFLICT (id) DO UPDATE SET task_id = $task, payload = $payload",
            ("$id", comment.Id), ("$task", comment.TaskId), ("$payload", Seal(CommentContext + comment.Id, comment)));
        return comment;
    }

    public TaskComment? GetComment(string id) => Find<TaskComment>("task_comments", CommentContext, id);

    public IReadOnlyList<TaskComment> ListComments(string taskId) =>
        ReadAll<TaskComment>(CommentContext, "SELECT id, payload FROM task_comments WHERE task_id = $task", ("$task", taskId));

    /// <summary>How many comments each task has, not counting deleted ones, for the speech bubbles in the list.</summary>
    public IReadOnlyDictionary<string, int> CommentCounts() =>
        ReadAll<TaskComment>(CommentContext, "SELECT id, payload FROM task_comments")
            .Where(c => c.Deleted is null).GroupBy(c => c.TaskId).ToDictionary(g => g.Key, g => g.Count());

    public TaskActivity SaveActivity(TaskActivity activity)
    {
        Run("INSERT INTO task_activity (id, task_id, payload) VALUES ($id, $task, $payload) ON CONFLICT (id) DO NOTHING",
            ("$id", activity.Id), ("$task", activity.TaskId), ("$payload", Seal(ActivityContext + activity.Id, activity)));
        return activity;
    }

    public IReadOnlyList<TaskActivity> ListActivity(string taskId) =>
        ReadAll<TaskActivity>(ActivityContext, "SELECT id, payload FROM task_activity WHERE task_id = $task", ("$task", taskId));
}
