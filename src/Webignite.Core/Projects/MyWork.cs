using Webignite.Core.Tasks;

namespace Webignite.Core.Projects;

/// <summary>One line in My tasks: a task given to me in a project, or one of my to-dos.</summary>
public sealed record MyItem(ProjectTask? Task, TaskItem? Todo)
{
    public DateOnly? Due => Task?.Due ?? Todo?.Due;
    public string Title => Task?.Title ?? Todo?.Title ?? "";
    public TaskPriority Priority => Task?.Priority ?? TaskPriority.Normal;
}

/// <summary>
/// My tasks, as Asana's: the tasks given to me in any project and my own to-dos (which belong to no project), in
/// one list by when they're due.
/// </summary>
public static class MyWork
{
    /// <summary>
    /// Overdue, Today, This week, Later and No due date, soonest first; to-dos already done are left out. The week
    /// starts on <paramref name="firstDay"/>; screens pass the PC's.
    /// </summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<MyItem> Items)> ByWhen(IEnumerable<ProjectTask> tasks, IEnumerable<TaskItem> todos, DateOnly today,
        DayOfWeek firstDay = DayOfWeek.Monday)
    {
        var weekEnd = Dates.WeekStart(today, firstDay).AddDays(6);
        static int Rank(TaskPriority p) => p switch { TaskPriority.High => 0, TaskPriority.Normal => 1, _ => 2 };
        var sorted = tasks.Select(t => new MyItem(t, null))
            .Concat(todos.Where(t => !t.Done).Select(t => new MyItem(null, t)))
            .OrderBy(i => i.Due ?? DateOnly.MaxValue).ThenBy(i => Rank(i.Priority)).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        (string, IReadOnlyList<MyItem>)[] groups =
        [
            ("Overdue", sorted.Where(i => i.Due < today).ToList()),
            ("Today", sorted.Where(i => i.Due == today).ToList()),
            ("This week", sorted.Where(i => i.Due > today && i.Due <= weekEnd).ToList()),
            ("Later", sorted.Where(i => i.Due > weekEnd).ToList()),
            ("No due date", sorted.Where(i => i.Due is null).ToList()),
        ];
        return [.. groups.Where(g => g.Item2.Count > 0)];
    }

    /// <summary>The line under My tasks' heading.</summary>
    public static string Summary(int tasks, int todos) => (tasks, todos) switch
    {
        (0, 0) => "Nothing on your plate right now.",
        (_, 0) => $"{Count(tasks, "task")} given to you, across your projects.",
        (0, _) => $"{Count(todos, "to-do")} of your own.",
        _ => $"{Count(tasks, "task")} given to you, and {Count(todos, "to-do")} of your own.",
    };

    private static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    /// <summary>
    /// A to-do moved into a project: a task under no heading there, given to me, with its words, due date and
    /// what it's about. The client is matched by name, as to-dos keep it.
    /// </summary>
    public static ProjectTask ToTask(TaskItem todo, string projectId, string meId, string? clientId, string newId) => new()
    {
        Id = newId,
        ProjectId = projectId,
        Title = todo.Title,
        Notes = todo.Notes ?? "",
        Due = todo.Due,
        AssignedTo = meId,
        ClientId = clientId,
        DocumentId = todo.DocumentId,
    };
}
