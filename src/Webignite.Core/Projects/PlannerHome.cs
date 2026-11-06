namespace Webignite.Core.Projects;

/// <summary>Which projects Home's Projects card shows, as Asana's choice above it: recent, starred, or all in my order.</summary>
public enum HomeProjects { Recent, Starred, Ordered }

/// <summary>How Home was left: what the Projects card shows.</summary>
public sealed record HomeSettings
{
    public HomeProjects Projects { get; init; }
}

/// <summary>
/// What Planner's Home shows: a greeting, my tasks in three tabs (Upcoming, Overdue, Completed), how many I
/// finished this week, and the projects worked on most lately.
/// </summary>
public static class PlannerHome
{
    public static string Greeting(int hour) => hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";

    /// <summary>My open tasks not yet late: soonest first, those with no date last.</summary>
    public static IReadOnlyList<ProjectTask> Upcoming(IEnumerable<ProjectTask> mine, DateOnly today) =>
        [.. mine.Where(t => !t.Done && !(t.Due < today)).OrderBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>My open tasks past their date, the latest first.</summary>
    public static IReadOnlyList<ProjectTask> Overdue(IEnumerable<ProjectTask> mine, DateOnly today) =>
        [.. mine.Where(t => !t.Done && t.Due < today).OrderBy(t => t.Due).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>When a task was ticked off: when its done field last changed, or when it last changed at all.</summary>
    public static DateTimeOffset DoneAt(ProjectTask task) => task.Fields.TryGetValue("done", out var stamp) ? stamp.At : task.Changed.At;

    /// <summary>Tasks given to me and ticked off since a moment, the most recent first.</summary>
    public static IReadOnlyList<ProjectTask> Completed(IEnumerable<ProjectTask> tasks, string meId, DateTimeOffset since) =>
        [.. tasks.Where(t => t.Deleted is null && t.Done && t.AssignedTo == meId && DoneAt(t) >= since).OrderByDescending(DoneAt)];

    /// <summary>The start of this week (Monday), for "tasks completed this week".</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset now)
    {
        var monday = Dates.WeekStart(DateOnly.FromDateTime(now.Date));
        return new DateTimeOffset(monday.ToDateTime(TimeOnly.MinValue), now.Offset);
    }

    /// <summary>The projects still going, the ones changed most lately first.</summary>
    public static IReadOnlyList<Project> Recent(IEnumerable<Project> projects, int count) =>
        [.. projects.Where(p => p.Deleted is null && p.Status != ProjectStatus.Done).OrderByDescending(p => p.Changed.At).Take(count)];

    /// <summary>
    /// The projects for Home's Projects card: the ones changed most lately, the starred ones, or all still going,
    /// in the order the sidebar has them (starred first). Only the last two can be put in order by dragging.
    /// </summary>
    public static IReadOnlyList<Project> Shown(IEnumerable<Project> projects, HomeProjects which, int count)
    {
        var going = projects.Where(p => p.Deleted is null && p.Status != ProjectStatus.Done).ToList();
        return which switch
        {
            HomeProjects.Starred => [.. ProjectLooks.SidebarOrder(going.Where(p => p.Favourite)).Take(count)],
            HomeProjects.Ordered => [.. ProjectLooks.SidebarOrder(going).Take(count)],
            _ => Recent(going, count),
        };
    }

    public static string Label(HomeProjects which) => which switch
    {
        HomeProjects.Starred => "Starred",
        HomeProjects.Ordered => "In my order",
        _ => "Recent",
    };
}
