using System.Globalization;

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
    /// <summary>"Next week" as a due date: the coming Monday. On a Sunday that is tomorrow; on a Monday, a week on.</summary>
    public static DateOnly NextMonday(DateOnly today)
    {
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }

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

    /// <summary>
    /// The line under a short to-do list. When nothing is due today or overdue it names the next thing coming
    /// up; otherwise it says which to-dos arrive by themselves. Null when there are no to-dos at all.
    /// </summary>
    public static string? Hint(IReadOnlyCollection<TaskItem> tasks, DateOnly today)
    {
        if (tasks.Count == 0)
            return null;
        var open = tasks.Where(t => !t.Done).ToList();
        var pressing = open.Any(t => Of(t, today) is TaskGroup.Overdue or TaskGroup.Today);
        var next = Sort(open.Where(t => Of(t, today) == TaskGroup.Upcoming)).FirstOrDefault();
        if (!pressing && next?.Due is { } due)
            return $"Nothing due today. Next up: {next.Title.Trim()}, {Day(due, today)}.";
        return "Overdue invoices, quotes waiting on a client and forgotten drafts land here by themselves.";
    }

    private static string Day(DateOnly due, DateOnly today) => Wording.Day(due, today);
}
