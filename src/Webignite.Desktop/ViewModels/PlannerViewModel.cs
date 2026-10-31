using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;
using Webignite.Core.Tasks;

namespace Webignite.Desktop.ViewModels;

/// <summary>A week at a glance: what's on each day, and the undated to-dos waiting for a slot.</summary>
public partial class PlannerViewModel : ViewModelBase
{
    private readonly AppServices services;
    private DateOnly weekStart;

    public PlannerViewModel(AppServices services)
    {
        this.services = services;
        weekStart = Agenda.StartOfWeek(Today);
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
        weekStart = Agenda.StartOfWeek(Today);
        Refresh();
    }

    public void Plan(TaskItem task, DateOnly day)
    {
        services.Store.SaveTask(task with { Due = day });
        Refresh();
    }

    public void Refresh()
    {
        var end = weekStart.AddDays(6);
        var tasks = services.Store.ListTasks();
        var items = Agenda.Build(weekStart, end, services.Store.ListEvents(weekStart, end), tasks, services.Store.List()).ToLookup(i => i.Date);

        WeekTitle = weekStart.Month == end.Month
            ? $"{weekStart.Day} to {end:d MMMM yyyy}"
            : $"{weekStart:d MMM} to {end:d MMM yyyy}";
        Days.Clear();
        for (var day = weekStart; day <= end; day = day.AddDays(1))
            Days.Add(new PlannerDayViewModel(day, day == Today, items[day].Select(i => new AgendaItemViewModel(i)).ToList()));

        var dayChoices = Days.Where(d => d.Date >= Today).Select(d => new DueChoice(d.Name, _ => d.Date)).ToList();
        Unplanned.Clear();
        foreach (var task in TaskGroups.Sort(tasks.Where(t => !t.Done && t.Due is null)))
            Unplanned.Add(new UnplannedTaskViewModel(task, dayChoices, this));
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

public sealed class UnplannedTaskViewModel(TaskItem task, IReadOnlyList<DueChoice> days, PlannerViewModel owner) : ViewModelBase
{
    public string Title => task.Title;
    public IReadOnlyList<DueChoice> Days { get; } = days;
    public bool CanPlan => Days.Count > 0;

    public DueChoice? PlanFor
    {
        get => null;
        set
        {
            if (value?.Pick(default) is { } day)
                owner.Plan(task, day);
        }
    }
}
