using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;
using Webignite.Core.Tasks;
using Webignite.Core;

namespace Webignite.Desktop.ViewModels;

/// <summary>A week at a glance: what's on each day, and the undated to-dos waiting for a slot.</summary>
public partial class PlannerViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices services;
    private DateOnly weekStart;

    /// <summary>How tall each day is, as dragged; NaN fits what's on the day (at least <see cref="DayMinHeight"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayMinHeight))]
    public partial double DayHeight { get; set; }

    /// <summary>Days fitting their content start a sensible height; dragged ones take exactly the height given.</summary>
    public double DayMinHeight => double.IsNaN(DayHeight) ? 260 : Webignite.Core.Layout.CalendarSizing.LeastPlannerDay;

    /// <summary>Keeps the days' height as dragged, or null to fit what's on them again.</summary>
    public void KeepDayHeight(double? height)
    {
        DayHeight = height is { } h ? Webignite.Core.Layout.CalendarSizing.PlannerDay(h) : double.NaN;
        services.Settings.CalendarSizes = services.Settings.CalendarSizes with { PlannerDayHeight = height is null ? null : DayHeight };
    }

    public PlannerViewModel(AppServices services)
    {
        this.services = services;
        DayHeight = services.Settings.CalendarSizes.PlannerDayHeight ?? double.NaN;
        weekStart = Dates.WeekStart(Today);
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public ObservableCollection<PlannerDayViewModel> Days { get; } = [];
    public ObservableCollection<UnplannedTaskViewModel> Unplanned { get; } = [];

    [ObservableProperty]
    public partial string WeekTitle { get; set; } = "";

    public bool HasUnplanned => Unplanned.Count > 0;

    [RelayCommand]
    private void PreviousWeek()
    {
        weekStart = weekStart.AddDays(-7);
        Refresh();
    }

    [RelayCommand]
    private void NextWeek()
    {
        weekStart = weekStart.AddDays(7);
        Refresh();
    }

    [RelayCommand]
    private void ThisWeek()
    {
        weekStart = Dates.WeekStart(Today);
        Refresh();
    }

    public void Plan(TaskItem task, DateOnly day)
    {
        services.Store.ChangeTask(task.Id, t => t with { Due = day });
        Refresh();
    }

    public void Refresh()
    {
        var end = weekStart.AddDays(6);
        var tasks = services.Store.ListTasks();
        var items = Agenda.Build(weekStart, end, services.Store.ListEvents(weekStart, end), tasks, services.Documents.List(),
            services.Projects.Agenda(weekStart, end, services.Me().Id)).ToLookup(i => i.Date);

        WeekTitle = weekStart.Month == end.Month
            ? $"{weekStart.Day} to {end:d MMMM yyyy}"
            : $"{weekStart:d MMM} to {end:d MMM yyyy}";
        Days.Clear();
        for (var day = weekStart; day <= end; day = day.AddDays(1))
            Days.Add(new PlannerDayViewModel(day, day == Today, items[day].Select(i => new AgendaItemViewModel(i)).ToList()));

        var dayChoices = Days.Where(d => d.Date >= Today).Select(d => new DueChoice(d.Name, _ => d.Date)).ToList();
        Unplanned.Clear();
        foreach (var task in TaskGroups.Sort(tasks.Where(t => !t.Done && t.Due is null)))
            Unplanned.Add(new UnplannedTaskViewModel(task.Title, "To-do", dayChoices, day => Plan(task, day)));
        // Tasks given to me in projects with no due date, as My tasks has them.
        var names = services.Projects.Projects().ToDictionary(p => p.Id, p => p.Name);
        foreach (var task in services.Projects.Mine(services.Me().Id).Where(t => t.Due is null).OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase))
            Unplanned.Add(new UnplannedTaskViewModel(task.Title, names.GetValueOrDefault(task.ProjectId) ?? "", dayChoices, day =>
            {
                services.Projects.ChangeTask(task.Id, t => t with { Due = day });
                Refresh();
            }));
        OnPropertyChanged(nameof(HasUnplanned));
    }
}

public sealed class PlannerDayViewModel(DateOnly date, bool isToday, IReadOnlyList<AgendaItemViewModel> items)
{
    public DateOnly Date { get; } = date;
    public string Name => Date.ToString("ddd d");
    public bool IsToday { get; } = isToday;
    public IReadOnlyList<AgendaItemViewModel> Items { get; } = items;
    public bool IsFree => Items.Count == 0;
}

/// <summary>A to-do, or a task given to me in a project, with no day yet: pick one to plan it.</summary>
public sealed class UnplannedTaskViewModel(string title, string about, IReadOnlyList<DueChoice> days, Action<DateOnly> plan) : ViewModelBase
{
    public string Title => title;
    public string About => about;
    public IReadOnlyList<DueChoice> Days { get; } = days;
    public bool CanPlan => Days.Count > 0;

    public DueChoice? PlanFor
    {
        get => null;
        set
        {
            if (value?.Pick(default) is { } day)
                plan(day);
        }
    }
}
