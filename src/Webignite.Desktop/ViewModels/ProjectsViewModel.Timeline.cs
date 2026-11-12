using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The Timeline and Calendar tabs, as Asana's. Timeline: each dated task is a bar from its start to its due date,
// under its heading; drag a bar to move it, its ends to stretch it, and tasks with no date from the Unscheduled list
// onto a day. Calendar: a month of weeks with each task on its due date; drag one to another day.
public sealed partial class ProjectsViewModel
{
    /// <summary>How wide a day is on the timeline, in pixels; the view snaps drags to it.</summary>
    public const double DayWidth = 36;

    // Timeline

    public ObservableCollection<TimelineDay> TimelineDays { get; } = [];
    public ObservableCollection<TimelineLaneRow> TimelineLanes { get; } = [];
    public ObservableCollection<TaskRow> Unscheduled { get; } = [];

    public bool NoUnscheduled => Unscheduled.Count == 0;
    public bool NoTimelineBars => TimelineLanes.Count == 0;

    [ObservableProperty] public partial double TimelineWidth { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodayPlace))]
    public partial double TodayLeft { get; private set; }

    public Avalonia.Thickness TodayPlace => new(TodayLeft - 1, 0, 0, 0);

    private DateOnly timelineFrom;

    /// <summary>How far the timeline is scrolled across; heading names and the month stay pinned at its left edge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaneTitlePlace), nameof(VisibleMonth))]
    public partial double TimelineScrollX { get; set; }

    public Avalonia.Thickness LaneTitlePlace => new(TimelineScrollX + 10, 8, 0, 2);

    /// <summary>The month of the first day in view, pinned at the top left.</summary>
    public string VisibleMonth => TimelineDays.Count == 0 ? ""
        : timelineFrom.AddDays((int)(TimelineScrollX / DayWidth)).ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    private void LoadTimeline(IReadOnlyList<ProjectTask> tasks, IReadOnlyList<Heading> headings, Func<ProjectTask, TaskRow> row)
    {
        TimelineDays.Clear();
        TimelineLanes.Clear();
        Unscheduled.Clear();
        if (!IsTimeline)
            return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (from, days) = ProjectTimeline.Range(tasks, today, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        timelineFrom = from;
        TimelineWidth = days * DayWidth;
        TodayLeft = (today.DayNumber - from.DayNumber) * DayWidth + DayWidth / 2;
        for (var i = 0; i < days; i++)
            TimelineDays.Add(new TimelineDay(from.AddDays(i), i == 0, today));
        foreach (var lane in ProjectTimeline.Lanes(headings, tasks, from))
            TimelineLanes.Add(new TimelineLaneRow(lane.Title, [.. lane.Bars.Select(b => new TimelineBarRow(row(b.Task), b, this))], this));
        foreach (var task in ProjectTimeline.Unscheduled(tasks))
            Unscheduled.Add(row(task));
        OnPropertyChanged(nameof(VisibleMonth));
        OnPropertyChanged(nameof(NoUnscheduled));
        OnPropertyChanged(nameof(NoTimelineBars));
    }

    /// <summary>A bar dragged by whole days: moved, or stretched at its start or end.</summary>
    public void DragBar(TimelineBarRow bar, int days, BarEdge edge)
    {
        if (days == 0)
            return;
        Func<ProjectTask, ProjectTask> change = edge switch
        {
            BarEdge.Start => t => ProjectTimeline.StretchStart(t, days),
            BarEdge.End => t => ProjectTimeline.StretchEnd(t, days),
            _ => t => ProjectTimeline.Shift(t, days),
        };
        ChangeTask(bar.Row, change);
        ShowTasks();
    }

    /// <summary>Takes a task's start and due date off, back to Unscheduled.</summary>
    public void ClearDates(TaskRow row)
    {
        ChangeTask(row, t => t with { Start = null, Due = null });
        ShowTasks();
    }

    /// <summary>A task with no date dropped on a day of the timeline: due that day.</summary>
    public void Schedule(TaskRow row, TimelineDay day)
    {
        ChangeTask(row, t => ProjectTimeline.DueOn(t, day.Day));
        ShowTasks();
    }

    // Calendar

    public ObservableCollection<CalendarCellRow> CalendarCells { get; } = [];

    /// <summary>The weekday names across the top, from the PC's first day of the week.</summary>
    public IReadOnlyList<string> WeekdayNames
    {
        get
        {
            var format = CultureInfo.CurrentCulture.DateTimeFormat;
            return [.. Enumerable.Range(0, 7).Select(i => format.AbbreviatedDayNames[((int)format.FirstDayOfWeek + i) % 7])];
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalendarTitle))]
    public partial DateOnly CalendarMonth { get; private set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public string CalendarTitle => CalendarMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void PreviousMonth()
    {
        CalendarMonth = CalendarMonth.AddMonths(-1);
        ShowTasks();
    }

    [RelayCommand]
    private void NextMonth()
    {
        CalendarMonth = CalendarMonth.AddMonths(1);
        ShowTasks();
    }

    [RelayCommand]
    private void ThisMonth()
    {
        CalendarMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        ShowTasks();
    }

    private void LoadCalendar(IReadOnlyList<ProjectTask> tasks, Func<ProjectTask, TaskRow> row)
    {
        CalendarCells.Clear();
        if (!IsCalendar)
            return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var cell in ProjectCalendar.Month(CalendarMonth.Year, CalendarMonth.Month, tasks, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek))
            CalendarCells.Add(new CalendarCellRow(cell, [.. cell.Tasks.Select(row)], cell.Day == today, this));
    }

    /// <summary>A task dropped on another day of the calendar: due then, keeping how many days it spans.</summary>
    public void MoveToDay(TaskRow row, DateOnly day)
    {
        if (row.Task.Due == day)
            return;
        ChangeTask(row, t => ProjectTimeline.DueOn(t, day));
        ShowTasks();
    }

    /// <summary>A task typed on a day of the calendar: under no heading, due that day.</summary>
    internal void AddOnDay(DateOnly day, string title)
    {
        if (open is null || title.Trim().Length == 0)
            return;
        var (saved, problems) = Ours(() => services.Projects.AddTask(ProjectRules.Filtered(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = open.Id, Title = title, Due = day,
        }, FilterPriority?.Value, FilterTag?.Value)));
        Message = saved is null ? string.Join(" ", problems) : "";
        if (saved is not null)
            ShowTasks();
    }
}

/// <summary>Which part of a timeline bar is dragged: the whole bar, or one of its ends.</summary>
public enum BarEdge { Move, Start, End }

/// <summary>A day across the top of the timeline: its number, and the month's name where a month starts.</summary>
public sealed class TimelineDay(DateOnly day, bool first, DateOnly today)
{
    public DateOnly Day { get; } = day;
    public string Number => Day.Day.ToString(CultureInfo.CurrentCulture);
    public string Initial => CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(Day.DayOfWeek);
    public string Month => first || Day.Day == 1 ? Day.ToString("MMMM yyyy", CultureInfo.CurrentCulture) : "";
    public bool HasMonth => Month.Length > 0;
    public bool IsWeekend => Day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool IsToday => Day == today;
    public static double Width => ProjectsViewModel.DayWidth;
}

/// <summary>A heading on the timeline, with a row per dated task.</summary>
public sealed record TimelineLaneRow(string Title, IReadOnlyList<TimelineBarRow> Bars, ProjectsViewModel Page);

/// <summary>A task's bar on the timeline: placed by day, its name inside when there's room, beside it when not.</summary>
public sealed class TimelineBarRow(TaskRow row, TimelineBar bar, ProjectsViewModel page)
{
    public TaskRow Row { get; } = row;
    public TimelineBar Bar { get; } = bar;
    public ProjectsViewModel Page { get; } = page;
    public double Left => Bar.Start * ProjectsViewModel.DayWidth + 2;
    public double Width => Bar.Length * ProjectsViewModel.DayWidth - 4;
    public Avalonia.Thickness Place => new(Left, 0, 0, 0);
    public bool NameInside => Bar.Length >= 4;
    public bool NameBeside => !NameInside;
    public string Title => Row.Title;
    public string Dates => Bar.Task.Start is { } start && start != Bar.Task.Due
        ? $"{start.ToString("d MMM", CultureInfo.CurrentCulture)} to {Bar.Task.Due!.Value.ToString("d MMM", CultureInfo.CurrentCulture)}"
        : Bar.Task.Due!.Value.ToString("d MMM", CultureInfo.CurrentCulture);
}

/// <summary>A day in the project's calendar: its number, its tasks (the first few) and a box to add one.</summary>
public sealed partial class CalendarCellRow(CalendarCell cell, IReadOnlyList<TaskRow> tasks, bool isToday, ProjectsViewModel page) : ObservableObject
{
    private const int Shown = 4;

    public DateOnly Day => cell.Day;
    public bool InMonth => cell.InMonth;
    public bool IsToday { get; } = isToday;
    public string Number => cell.Day.Day == 1 ? cell.Day.ToString("d MMM", CultureInfo.CurrentCulture) : cell.Day.Day.ToString(CultureInfo.CurrentCulture);
    public IReadOnlyList<TaskRow> Tasks { get; } = [.. tasks.Take(Shown)];
    public string More => tasks.Count > Shown ? $"{tasks.Count - Shown} more" : "";
    public bool HasMore => tasks.Count > Shown;

    [ObservableProperty] public partial string NewTitle { get; set; } = "";

    [RelayCommand]
    private void Add()
    {
        var title = NewTitle;
        NewTitle = "";
        page.AddOnDay(Day, title);
    }
}
