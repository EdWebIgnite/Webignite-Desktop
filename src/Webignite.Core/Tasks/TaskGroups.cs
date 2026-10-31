namespace Webignite.Core.Tasks;

public enum TaskGroup
{
    Overdue,
    Today,
    Upcoming,
    Someday,
    Done,
}

public static class TaskGroups
{
    public static TaskGroup Of(TaskItem task, DateOnly today) => task switch
    {
        { Done: true } => TaskGroup.Done,
        { Due: null } => TaskGroup.Someday,
        { Due: var due } when due < today => TaskGroup.Overdue,
        { Due: var due } when due == today => TaskGroup.Today,
        _ => TaskGroup.Upcoming,
    };

    /// <summary>Soonest first, then oldest first; done tasks newest first.</summary>
    public static IEnumerable<TaskItem> Sort(IEnumerable<TaskItem> tasks) =>
        tasks.OrderBy(t => t.Done)
            .ThenBy(t => t.Done ? DateTimeOffset.MaxValue - (t.DoneAt ?? t.Created) : TimeSpan.Zero)
            .ThenBy(t => t.Due ?? DateOnly.MaxValue)
            .ThenBy(t => t.Created);
}
