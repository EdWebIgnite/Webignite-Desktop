using Webignite.Core.Projects;

namespace Webignite.Tests;

public class ProjectTimelineTests
{
    private static readonly DateOnly Today = new(2026, 11, 6); // a Friday

    private static ProjectTask Task(string title, DateOnly? start = null, DateOnly? due = null, string? heading = null, string? parent = null, double order = 0) =>
        new() { Id = title, ProjectId = "p", Title = title, Start = start, Due = due, HeadingId = heading, ParentId = parent, Order = order };

    [Fact]
    public void The_timeline_starts_on_a_monday_a_week_early_and_runs_at_least_six_weeks()
    {
        var (from, days) = ProjectTimeline.Range([], Today);
        Assert.Equal(new DateOnly(2026, 10, 26), from);
        Assert.Equal(DayOfWeek.Monday, from.DayOfWeek);
        Assert.Equal(42, days);

        var (later, longer) = ProjectTimeline.Range([Task("Launch", due: new DateOnly(2027, 2, 1))], Today);
        Assert.Equal(from, later);
        Assert.Equal(new DateOnly(2027, 2, 15), later.AddDays(longer - 1));
    }

    [Fact]
    public void A_bar_runs_from_start_to_due_and_tasks_without_a_date_wait_aside()
    {
        var from = new DateOnly(2026, 11, 2);
        var bar = ProjectTimeline.Bar(Task("Build", start: new DateOnly(2026, 11, 4), due: new DateOnly(2026, 11, 6)), from)!;
        Assert.Equal((2, 3), (bar.Start, bar.Length));
        Assert.Equal((4, 1), (ProjectTimeline.Bar(Task("Call", due: Today), from)!.Start, ProjectTimeline.Bar(Task("Call", due: Today), from)!.Length));
        Assert.Equal(1, ProjectTimeline.Bar(Task("Odd", start: Today.AddDays(3), due: Today), from)!.Length);
        Assert.Null(ProjectTimeline.Bar(Task("Someday"), from));

        var headings = new[] { new Heading { Id = "h1", ProjectId = "p", Name = "Design", Order = 1 }, new Heading { Id = "h2", ProjectId = "p", Name = "Empty", Order = 2 } };
        var tasks = new[] { Task("Brief", due: Today), Task("Wireframes", due: Today, heading: "h1"), Task("Mobile", due: Today, heading: "h1", parent: "Wireframes"), Task("Someday", heading: "h2") };
        Assert.Equal(["No heading", "Design"], ProjectTimeline.Lanes(headings, tasks, from).Select(l => l.Title));
        Assert.Single(ProjectTimeline.Lanes(headings, tasks, from)[1].Bars);
        Assert.Equal(["Someday"], ProjectTimeline.Unscheduled(tasks).Select(t => t.Title));
    }

    [Fact]
    public void Dragging_moves_or_stretches_a_day_at_a_time_and_a_bar_keeps_a_day()
    {
        var task = Task("Build", start: new DateOnly(2026, 11, 4), due: new DateOnly(2026, 11, 6));
        var moved = ProjectTimeline.Shift(task, 3);
        Assert.Equal((new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 9)), (moved.Start!.Value, moved.Due!.Value));

        Assert.Equal(new DateOnly(2026, 11, 10), ProjectTimeline.StretchEnd(task, 4).Due);
        Assert.Equal(new DateOnly(2026, 11, 4), ProjectTimeline.StretchEnd(task, -9).Due);
        Assert.Equal(new DateOnly(2026, 11, 1), ProjectTimeline.StretchStart(task, -3).Start);
        Assert.Null(ProjectTimeline.StretchStart(task, 5).Start);
        Assert.Equal(new DateOnly(2026, 11, 4), ProjectTimeline.StretchEnd(Task("Call", due: new DateOnly(2026, 11, 3)) with { Start = null }, 1).Due);

        var dropped = ProjectTimeline.DueOn(task, new DateOnly(2026, 11, 20));
        Assert.Equal((new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 20)), (dropped.Start!.Value, dropped.Due!.Value));
        Assert.Equal(Today, ProjectTimeline.DueOn(Task("Someday"), Today).Due);
    }

    [Fact]
    public void Stretching_a_one_day_bar_with_no_start_later_grows_it_from_its_day()
    {
        var call = Task("Call", due: new DateOnly(2026, 11, 3));
        var longer = ProjectTimeline.StretchEnd(call, 2);
        Assert.Equal((new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5)), (longer.Start!.Value, longer.Due!.Value));
        Assert.Equal(3, ProjectTimeline.Bar(longer, new DateOnly(2026, 11, 2))!.Length);
        // Earlier than its one day it can't go: it stays put, with no start.
        var same = ProjectTimeline.StretchEnd(call, -2);
        Assert.Equal((null, new DateOnly(2026, 11, 3)), (same.Start, same.Due!.Value));
    }

    [Fact]
    public void A_task_with_a_start_but_no_due_date_dropped_on_a_day_keeps_its_start_when_it_can()
    {
        var started = Task("Build", start: new DateOnly(2026, 11, 4));
        var dropped = ProjectTimeline.DueOn(started, new DateOnly(2026, 11, 10));
        Assert.Equal((new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 10)), (dropped.Start!.Value, dropped.Due!.Value));
        Assert.Equal(new DateOnly(2026, 11, 4), ProjectTimeline.DueOn(started, new DateOnly(2026, 11, 4)).Start);
        // Dropped before its start, the start can't stay: it's due that day alone.
        var early = ProjectTimeline.DueOn(started, new DateOnly(2026, 11, 2));
        Assert.Equal((null, new DateOnly(2026, 11, 2)), (early.Start, early.Due!.Value));
    }

    [Fact]
    public void A_month_is_six_weeks_from_the_weeks_first_day_with_tasks_on_their_due_dates()
    {
        var tasks = new[] { Task("Launch", due: new DateOnly(2026, 11, 30)), Task("Brief", due: new DateOnly(2026, 11, 3), order: 2), Task("Call", due: new DateOnly(2026, 11, 3), order: 1) };
        var month = ProjectCalendar.Month(2026, 11, tasks, DayOfWeek.Monday);
        Assert.Equal(42, month.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), month[0].Day);
        Assert.False(month[0].InMonth);
        Assert.Equal(["Call", "Brief"], month.Single(c => c.Day == new DateOnly(2026, 11, 3)).Tasks.Select(t => t.Title));
        Assert.Equal(new DateOnly(2026, 11, 1), ProjectCalendar.Month(2026, 11, tasks, DayOfWeek.Sunday)[0].Day);
    }
}
