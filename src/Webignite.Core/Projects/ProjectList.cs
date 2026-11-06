namespace Webignite.Core.Projects;

/// <summary>How a project's list is sorted, as Asana's Sort menu. None keeps the order tasks were dragged into.</summary>
public enum TaskSort { None, DueDate, Assignee, Alphabetical, Priority, Created }

/// <summary>How a project's list is grouped, as Asana's Group menu. Sections are the project's own headings.</summary>
public enum TaskGroup { Sections, DueDate, Assignee, Priority }

/// <summary>
/// How one project was left: list or board, its sort and grouping, and which headings are folded up. Each project
/// keeps its own, as in Asana, so a board project opens on its board.
/// </summary>
public sealed record ProjectLayout
{
    public bool Board { get; init; }
    public TaskSort Sort { get; init; }
    public TaskGroup Group { get; init; }

    /// <summary>Headings folded up to their name, by id; the tasks under no heading as "".</summary>
    public IReadOnlyList<string> Folded { get; init; } = [];
}

/// <summary>A group in the list: a heading, or a group made by the Group menu (a person, a priority, when it's due).</summary>
public sealed record ListGroup(string Key, string Title, Heading? Heading, IReadOnlyList<ProjectTask> Tasks)
{
    /// <summary>A project heading (or the tasks under none) rather than a group the menu made.</summary>
    public bool IsSection { get; init; } = true;
}

/// <summary>The rules for a project's list as Asana lays it out: sorting, grouping and folding.</summary>
public static class ProjectList
{
    /// <summary>Tasks can be dragged into order only when the list isn't sorted or grouped by something else, as in Asana.</summary>
    public static bool CanReorder(ProjectLayout layout) => layout.Sort == TaskSort.None && layout.Group == TaskGroup.Sections;

    public static string Label(TaskSort sort) => sort switch
    {
        TaskSort.DueDate => "Due date",
        TaskSort.Assignee => "Assignee",
        TaskSort.Alphabetical => "Alphabetical",
        TaskSort.Priority => "Priority",
        TaskSort.Created => "Created on",
        _ => "None",
    };

    public static string Label(TaskGroup group) => group switch
    {
        TaskGroup.DueDate => "Due date",
        TaskGroup.Assignee => "Assignee",
        TaskGroup.Priority => "Priority",
        _ => "Sections",
    };

    /// <summary>Folds a heading up, or opens it out again.</summary>
    public static ProjectLayout Fold(ProjectLayout layout, string key, bool folded) => layout with
    {
        Folded = folded ? [.. layout.Folded.Where(k => k != key), key] : [.. layout.Folded.Where(k => k != key)],
    };

    /// <summary>
    /// The project's top-level tasks in groups: its headings (the tasks under none first, when there are some), or
    /// groups by when they're due, who they're for or their priority. Within a group, the sort picked, else the
    /// order they were dragged into. <paramref name="nameOf"/> gives a teammate's name by id.
    /// </summary>
    public static IReadOnlyList<ListGroup> Arrange(IEnumerable<Heading> headings, IEnumerable<ProjectTask> tasks, ProjectLayout layout,
        Func<string?, string?> nameOf, DateOnly today)
    {
        var top = tasks.Where(t => t.Deleted is null && t.ParentId is null).ToList();
        IReadOnlyList<ProjectTask> Sorted(IEnumerable<ProjectTask> list) => Sort(list, layout.Sort, nameOf);
        switch (layout.Group)
        {
            case TaskGroup.DueDate:
                return [.. ProjectRules.ByWhen(top, today).Select(g => new ListGroup("due:" + g.Title, g.Title, null, Sorted(g.Tasks)) { IsSection = false })];
            case TaskGroup.Assignee:
                return [.. top.GroupBy(t => nameOf(t.AssignedTo) ?? "")
                    .OrderBy(g => g.Key.Length == 0).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new ListGroup("who:" + g.Key, g.Key.Length == 0 ? "No one" : g.Key, null, Sorted(g)) { IsSection = false })];
            case TaskGroup.Priority:
                return [.. new[] { TaskPriority.High, TaskPriority.Normal, TaskPriority.Low }
                    .Select(p => (p, list: top.Where(t => t.Priority == p).ToList()))
                    .Where(g => g.list.Count > 0)
                    .Select(g => new ListGroup("priority:" + g.p, ProjectRules.Label(g.p) is { Length: > 0 } label ? label : "Normal", null, Sorted(g.list)) { IsSection = false })];
            default:
                return [.. ProjectRules.Sections(headings, top).Select(s => new ListGroup(s.Heading?.Id ?? "", s.Heading?.Name ?? "No heading", s.Heading,
                    layout.Sort == TaskSort.None ? s.Tasks : Sorted(s.Tasks)))];
        }
    }

    /// <summary>Tasks in the order picked; ties keep the dragged order.</summary>
    public static IReadOnlyList<ProjectTask> Sort(IEnumerable<ProjectTask> tasks, TaskSort sort, Func<string?, string?> nameOf)
    {
        var list = tasks.OrderBy(t => t.Order);
        static int Rank(TaskPriority p) => p switch { TaskPriority.High => 0, TaskPriority.Normal => 1, _ => 2 };
        return sort switch
        {
            TaskSort.DueDate => [.. list.OrderBy(t => t.Due is null).ThenBy(t => t.Due)],
            TaskSort.Assignee => [.. list.OrderBy(t => nameOf(t.AssignedTo) is null).ThenBy(t => nameOf(t.AssignedTo), StringComparer.CurrentCultureIgnoreCase)],
            TaskSort.Alphabetical => [.. list.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)],
            TaskSort.Priority => [.. list.OrderBy(t => Rank(t.Priority))],
            TaskSort.Created => [.. list.OrderBy(t => t.Created.At)],
            _ => [.. list],
        };
    }
}
