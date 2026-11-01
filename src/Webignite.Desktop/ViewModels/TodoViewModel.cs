using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tasks;

namespace Webignite.Desktop.ViewModels;

public sealed record DueChoice(string Label, Func<DateOnly, DateOnly?> Pick)
{
    public override string ToString() => Label;
}

public partial class TodoViewModel : ViewModelBase
{
    private readonly AppServices services;

    public TodoViewModel(AppServices services)
    {
        this.services = services;
        DueChoices =
        [
            new("No date", _ => null),
            new("Today", today => today),
            new("Tomorrow", today => today.AddDays(1)),
            new("Next week", today => today.AddDays(7 - (int)today.DayOfWeek + 1)),
        ];
        NewDue = DueChoices[1];
        Refresh();
    }

    public IReadOnlyList<DueChoice> DueChoices { get; }
    public ObservableCollection<TaskGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewTitle { get; set; } = "";

    [ObservableProperty]
    public partial DueChoice NewDue { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewTitle);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        services.Store.SaveTask(new TaskItem
        {
            Id = TaskItem.NewId(),
            Title = NewTitle.Trim(),
            Due = NewDue.Pick(Today),
            Created = DateTimeOffset.UtcNow,
        });
        NewTitle = "";
        Refresh();
    }

    public void Toggle(TaskItem task, bool done)
    {
        services.Store.SaveTask(task with { Done = done, DoneAt = done ? DateTimeOffset.UtcNow : null });
        Refresh();
    }

    public void Delete(TaskItem task)
    {
        services.Store.DeleteTask(task.Id);
        Refresh();
    }

    /// <summary>Raises any new automatic reminders, then rebuilds the groups.</summary>
    public void Refresh()
    {
        services.Store.RaiseAutomaticTasks(services.TaskSettings, Today);
        var tasks = TaskGroups.Sort(services.Store.ListTasks()).ToList();
        Groups.Clear();
        foreach (var group in tasks.GroupBy(t => TaskGroups.Of(t, Today)).OrderBy(g => g.Key))
        {
            var items = group.Key == TaskGroup.Done ? group.Take(20) : group;
            Groups.Add(new TaskGroupViewModel(group.Key, items.Select(t => new TaskRowViewModel(t, this)).ToList()));
        }
        var open = tasks.Count(t => !t.Done);
        var overdue = tasks.Count(t => TaskGroups.Of(t, Today) == TaskGroup.Overdue);
        Summary = open == 0 ? "Nothing to do. Nice." : overdue > 0 ? $"{open} open, {overdue} overdue" : $"{open} open";
    }
}

public sealed class TaskGroupViewModel(TaskGroup group, IReadOnlyList<TaskRowViewModel> items)
{
    public string Title => group switch
    {
        TaskGroup.Overdue => "Overdue",
        TaskGroup.Today => "Today",
        TaskGroup.Upcoming => "Coming up",
        TaskGroup.Someday => "No date",
        _ => "Done",
    };

    public bool IsOverdue => group == TaskGroup.Overdue;
    public IReadOnlyList<TaskRowViewModel> Items { get; } = items;
}

public partial class TaskRowViewModel : ViewModelBase
{
    private readonly TodoViewModel owner;

    public TaskRowViewModel(TaskItem task, TodoViewModel owner)
    {
        Task = task;
        this.owner = owner;
        isDone = task.Done;
    }

    public TaskItem Task { get; }
    public string Title => Task.Title;
    public bool IsAuto => Task.Source == TaskSource.Auto;
    public string SourceLabel => Task.Source switch
    {
        TaskSource.Auto => "Automatic",
        TaskSource.Claude => "From Claude",
        TaskSource.ChatGpt => "From ChatGPT",
        _ => "",
    };
    public bool HasSource => SourceLabel.Length > 0;

    public string DueText
    {
        get
        {
            if (Task.Due is not { } due)
                return "";
            var today = DateOnly.FromDateTime(DateTime.Today);
            var days = due.DayNumber - today.DayNumber;
            return days switch
            {
                0 => "Today",
                1 => "Tomorrow",
                -1 => "Yesterday",
                < 0 => $"{-days} days ago",
                < 7 => due.ToString("dddd"),
                _ => due.ToString("d MMM"),
            };
        }
    }

    private bool isDone;

    public bool IsDone
    {
        get => isDone;
        set
        {
            if (SetProperty(ref isDone, value))
                owner.Toggle(Task, value);
        }
    }

    [RelayCommand]
    private void Delete() => owner.Delete(Task);
}
