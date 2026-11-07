namespace Webignite.Core.Projects;

/// <summary>Which projects Home's Projects card shows, as Asana's choice above it: recent, starred, or all in my order.</summary>
public enum HomeProjects { Recent, Starred, Ordered }

/// <summary>What a widget on Home shows, as Asana's Customise panel offers.</summary>
public enum HomeWidgetKind { MyTasks, Projects, Notepad, People, StatusUpdates, Milestones }

/// <summary>A widget on Home: what it shows, and whether it takes the full width or half.</summary>
public sealed record HomeWidget(HomeWidgetKind Kind, bool Full = false);

/// <summary>How Home was left: its widgets in order, what the Projects card shows, and the private notepad's words.</summary>
public sealed record HomeSettings
{
    public HomeProjects Projects { get; init; }

    /// <summary>The widgets in order; null for a Home never customised, which shows My tasks and Projects.</summary>
    public IReadOnlyList<HomeWidget>? Widgets { get; init; }

    public string Notepad { get; init; } = "";
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

    /// <summary>My open tasks and to-dos not yet late, as My tasks has them: soonest first, those with no date last.</summary>
    public static IReadOnlyList<MyItem> Upcoming(IEnumerable<ProjectTask> mine, IEnumerable<Tasks.TaskItem> todos, DateOnly today) =>
        [.. Mix(mine, todos).Where(i => !(i.Due < today)).OrderBy(i => i.Due ?? DateOnly.MaxValue).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>My open tasks and to-dos past their date, the latest first.</summary>
    public static IReadOnlyList<MyItem> Overdue(IEnumerable<ProjectTask> mine, IEnumerable<Tasks.TaskItem> todos, DateOnly today) =>
        [.. Mix(mine, todos).Where(i => i.Due < today).OrderBy(i => i.Due).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Tasks given to me and to-dos ticked off since a moment, the most recent first.</summary>
    public static IReadOnlyList<MyItem> Completed(IEnumerable<ProjectTask> tasks, IEnumerable<Tasks.TaskItem> todos, string meId, DateTimeOffset since) =>
        [.. Completed(tasks, meId, since).Select(t => (Item: new MyItem(t, null), At: DoneAt(t)))
            .Concat(todos.Where(t => t.Done && (t.DoneAt ?? t.Created) >= since).Select(t => (Item: new MyItem(null, t), At: t.DoneAt ?? t.Created)))
            .OrderByDescending(x => x.At).Select(x => x.Item)];

    private static IEnumerable<MyItem> Mix(IEnumerable<ProjectTask> mine, IEnumerable<Tasks.TaskItem> todos) =>
        mine.Where(t => !t.Done).Select(t => new MyItem(t, null)).Concat(todos.Where(t => !t.Done).Select(t => new MyItem(null, t)));

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

    /// <summary>The widgets a Home starts with, as Asana's: My tasks and Projects side by side.</summary>
    public static IReadOnlyList<HomeWidget> DefaultWidgets { get; } = [new(HomeWidgetKind.MyTasks), new(HomeWidgetKind.Projects)];

    /// <summary>Home's widgets as kept: the default for one never customised, each kind once, unknown kinds dropped.</summary>
    public static IReadOnlyList<HomeWidget> Widgets(HomeSettings settings) =>
        settings.Widgets is null ? DefaultWidgets
        : [.. settings.Widgets.Where(w => Enum.IsDefined(w.Kind)).DistinctBy(w => w.Kind)];

    /// <summary>A widget added at the end, half width; one already there stays as it is.</summary>
    public static HomeSettings Add(HomeSettings settings, HomeWidgetKind kind)
    {
        var widgets = Widgets(settings);
        return widgets.Any(w => w.Kind == kind) ? settings with { Widgets = widgets } : settings with { Widgets = [.. widgets, new HomeWidget(kind)] };
    }

    public static HomeSettings Remove(HomeSettings settings, HomeWidgetKind kind) =>
        settings with { Widgets = [.. Widgets(settings).Where(w => w.Kind != kind)] };

    /// <summary>A widget made full width, or half again.</summary>
    public static HomeSettings Resize(HomeSettings settings, HomeWidgetKind kind) =>
        settings with { Widgets = [.. Widgets(settings).Select(w => w.Kind == kind ? w with { Full = !w.Full } : w)] };

    /// <summary>A widget dragged before or after another.</summary>
    public static HomeSettings Move(HomeSettings settings, HomeWidgetKind kind, HomeWidgetKind target, bool after)
    {
        var widgets = Widgets(settings).ToList();
        var moving = widgets.FirstOrDefault(w => w.Kind == kind);
        if (moving is null || kind == target || widgets.All(w => w.Kind != target))
            return settings with { Widgets = widgets };
        widgets.Remove(moving);
        var at = widgets.FindIndex(w => w.Kind == target) + (after ? 1 : 0);
        widgets.Insert(at, moving);
        return settings with { Widgets = widgets };
    }

    public static string Label(HomeWidgetKind kind) => kind switch
    {
        HomeWidgetKind.MyTasks => "My tasks",
        HomeWidgetKind.Projects => "Projects",
        HomeWidgetKind.Notepad => "Private notepad",
        HomeWidgetKind.People => "People",
        HomeWidgetKind.StatusUpdates => "Status updates",
        _ => "Milestones",
    };

    public static string About(HomeWidgetKind kind) => kind switch
    {
        HomeWidgetKind.MyTasks => "Your tasks coming up, overdue and done",
        HomeWidgetKind.Projects => "Projects worked on lately, starred or in your order",
        HomeWidgetKind.Notepad => "Jot things down; only on this PC",
        HomeWidgetKind.People => "You and your partner, with what's on each plate",
        HomeWidgetKind.StatusUpdates => "The latest word on how each project is going",
        _ => "The milestones coming up across your projects",
    };

    /// <summary>Each project's newest status update, the newest first.</summary>
    public static IReadOnlyList<(Project Project, StatusUpdate Update)> LatestUpdates(IEnumerable<Project> projects, int count) =>
        [.. projects.Where(p => p.Deleted is null && p.Updates.Count > 0).Select(p => (p, p.Updates[0])).OrderByDescending(x => x.Item2.At).Take(count)];

    /// <summary>Open milestones in projects still going, the soonest first, those with no date last.</summary>
    public static IReadOnlyList<ProjectTask> Milestones(IEnumerable<Project> projects, IEnumerable<ProjectTask> tasks, int count)
    {
        var going = projects.Where(p => p.Deleted is null && p.Status != ProjectStatus.Done).Select(p => p.Id).ToHashSet();
        return [.. ProjectOverview.Milestones(tasks.Where(t => going.Contains(t.ProjectId) && !t.Done)).Take(count)];
    }

    /// <summary>
    /// What's on someone's plate: their open tasks and how many are late, counted as My tasks counts them, so
    /// tasks in projects that are done or deleted are left out.
    /// </summary>
    public static (int Open, int Late) Load(IEnumerable<Project> projects, IEnumerable<ProjectTask> tasks, string personId, DateOnly today)
    {
        var open = ProjectRules.Mine(tasks, projects, personId);
        return (open.Count, open.Count(t => t.Due < today));
    }

    public static string Label(HomeProjects which) => which switch
    {
        HomeProjects.Starred => "Starred",
        HomeProjects.Ordered => "In my order",
        _ => "Recent",
    };
}
