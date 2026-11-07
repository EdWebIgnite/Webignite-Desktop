namespace Webignite.Core.Projects;

/// <summary>A task drawn on the timeline: which day its bar starts on, counted from the timeline's first day, and how many days it spans.</summary>
public sealed record TimelineBar(ProjectTask Task, int Start, int Length);

/// <summary>A heading's dated tasks on the timeline, one row each.</summary>
public sealed record TimelineLane(string Key, string Title, IReadOnlyList<TimelineBar> Bars);

/// <summary>
/// The rules for a project's Timeline tab, as Asana's: each dated task is a bar from its start to its due date, under
/// its heading, and dragging moves or stretches it a day at a time.
/// </summary>
public static class ProjectTimeline
{
    /// <summary>
    /// The days the timeline shows: from the Monday a week before the earliest date (or today), to two weeks past the
    /// latest, and never fewer than six weeks, so there's room to drag.
    /// </summary>
    public static (DateOnly From, int Days) Range(IEnumerable<ProjectTask> tasks, DateOnly today)
    {
        var dates = tasks.Where(t => t.Deleted is null).SelectMany(t => new[] { t.Start, t.Due }).OfType<DateOnly>().Append(today).ToList();
        var first = dates.Min().AddDays(-7);
        var from = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var to = dates.Max().AddDays(14);
        return (from, Math.Max(42, to.DayNumber - from.DayNumber + 1));
    }

    /// <summary>A task's bar, or null when it has no due date. A start after the due date is read as the due date.</summary>
    public static TimelineBar? Bar(ProjectTask task, DateOnly from)
    {
        if (task.Due is not { } due)
            return null;
        var start = task.Start is { } s && s <= due ? s : due;
        return new TimelineBar(task, start.DayNumber - from.DayNumber, due.DayNumber - start.DayNumber + 1);
    }

    /// <summary>The headings in order (the tasks under none first), each with its dated tasks; subtasks aren't drawn.</summary>
    public static IReadOnlyList<TimelineLane> Lanes(IEnumerable<Heading> headings, IEnumerable<ProjectTask> tasks, DateOnly from) =>
        [.. ProjectRules.Sections(headings, tasks.Where(t => t.Deleted is null && t.ParentId is null))
            .Select(s => new TimelineLane(s.Heading?.Id ?? "", s.Heading?.Name ?? "No heading",
                [.. s.Tasks.Select(t => Bar(t, from)).OfType<TimelineBar>()]))
            .Where(l => l.Bars.Count > 0)];

    /// <summary>Tasks with no due date, waiting to be put on the timeline, as Asana's Unscheduled list.</summary>
    public static IReadOnlyList<ProjectTask> Unscheduled(IEnumerable<ProjectTask> tasks) =>
        [.. tasks.Where(t => t.Deleted is null && t.ParentId is null && t.Due is null).OrderBy(t => t.Order)];

    /// <summary>The task moved by whole days, its start and due date together.</summary>
    public static ProjectTask Shift(ProjectTask task, int days) => days == 0 ? task : task with
    {
        Start = task.Start?.AddDays(days),
        Due = task.Due?.AddDays(days),
    };

    /// <summary>
    /// Its due date dragged by whole days; never before its start, so the bar keeps at least a day. A one-day bar
    /// with no start stretched later starts on the day it was due, so it grows rather than moves.
    /// </summary>
    public static ProjectTask StretchEnd(ProjectTask task, int days)
    {
        if (task.Due is not { } due)
            return task;
        var start = task.Start is { } s && s <= due ? s : due;
        var moved = due.AddDays(days);
        if (moved <= start)
            return task with { Due = start };
        return task with { Start = start, Due = moved };
    }

    /// <summary>Its start dragged by whole days; never after its due date. A start on the due date is dropped, as a one-day bar.</summary>
    public static ProjectTask StretchStart(ProjectTask task, int days)
    {
        if (task.Due is not { } due)
            return task;
        var moved = (task.Start ?? due).AddDays(days);
        if (moved > due)
            moved = due;
        return task with { Start = moved == due ? null : moved };
    }

    /// <summary>
    /// A task dropped on a day: due then, keeping how many days it spans. One with a start but no due date keeps
    /// its start when that's on or before the day.
    /// </summary>
    public static ProjectTask DueOn(ProjectTask task, DateOnly day) => task.Due is { } due
        ? Shift(task, day.DayNumber - due.DayNumber)
        : task with { Due = day, Start = task.Start is { } start && start <= day ? start : null };
}

/// <summary>A day in the project's Calendar tab, with the tasks due on it.</summary>
public sealed record CalendarCell(DateOnly Day, bool InMonth, IReadOnlyList<ProjectTask> Tasks);

/// <summary>The rules for a project's Calendar tab, as Asana's: a month of weeks, each task on its due date.</summary>
public static class ProjectCalendar
{
    /// <summary>Six weeks starting on the week's first day, holding the month; each day with its tasks, subtasks too, in order.</summary>
    public static IReadOnlyList<CalendarCell> Month(int year, int month, IEnumerable<ProjectTask> tasks, DayOfWeek firstDay)
    {
        var first = new DateOnly(year, month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek - (int)firstDay + 7) % 7));
        var byDay = tasks.Where(t => t.Deleted is null && t.Due is not null).GroupBy(t => t.Due!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Done).ThenBy(t => t.Order).ToList());
        return [.. Enumerable.Range(0, 42).Select(i =>
        {
            var day = start.AddDays(i);
            return new CalendarCell(day, day.Month == month, byDay.TryGetValue(day, out var list) ? list : []);
        })];
    }
}
