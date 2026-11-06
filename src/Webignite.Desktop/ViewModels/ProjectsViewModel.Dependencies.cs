using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// Dependencies, as Asana's: in the task pane, the tasks it's waiting on (add from a menu of the project's tasks,
// take one off) and the ones it holds up. A task still waiting shows a small sign in the list, on its card and
// in the calendar.
public sealed partial class TaskDetail
{
    public ObservableCollection<DependencyRow> WaitingOn { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlocking))]
    public partial string BlockingText { get; private set; } = "";

    public bool HasBlocking => BlockingText.Length > 0;

    private void LoadDependencies()
    {
        var tasks = services.Projects.AllTasks(Task.ProjectId);
        WaitingOn.Clear();
        foreach (var blocker in TaskDependencies.BlockedBy(Task, tasks))
            WaitingOn.Add(new DependencyRow(blocker, this));
        var blocking = TaskDependencies.Blocking(Task, tasks);
        BlockingText = blocking.Count == 0 ? "" : "Holding up: " + string.Join(", ", blocking.Select(t => t.Title));
    }

    /// <summary>The project's tasks this one could wait on, for the Add menu.</summary>
    public IReadOnlyList<ProjectTask> DependencyChoices() => TaskDependencies.Choices(Task, services.Projects.AllTasks(Task.ProjectId));

    public void AddDependency(string blockerId)
    {
        var (saved, problems) = page.Ours(() => services.Projects.AddDependency(Task.Id, blockerId));
        Problem = saved is null ? string.Join(" ", problems) : "";
        AfterDependencies();
    }

    internal void RemoveDependency(DependencyRow row)
    {
        page.Ours(() => services.Projects.RemoveDependency(Task.Id, row.Task.Id));
        AfterDependencies();
    }

    internal void OpenTask(string id) => page.OpenTaskById(id);

    private void AfterDependencies()
    {
        if (services.Projects.GetTask(Task.Id) is { } fresh)
            Task = fresh;
        LoadDependencies();
        page.Refresh();
    }
}

/// <summary>A task the open one is waiting on: its name (click to open it), whether it's done, and a cross to take it off.</summary>
public sealed partial class DependencyRow(ProjectTask task, TaskDetail detail) : ObservableObject
{
    public ProjectTask Task { get; } = task;
    public string Title => Task.Title;
    public bool Done => Task.Done;
    public bool Open => !Task.Done;

    [RelayCommand]
    private void Remove() => detail.RemoveDependency(this);

    [RelayCommand]
    private void Show() => detail.OpenTask(Task.Id);
}
