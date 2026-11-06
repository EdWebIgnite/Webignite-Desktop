using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The list as Asana's: sorted and grouped as picked, headings folded up, tasks opened out to show their subtasks.
// Each project keeps its own list or board, sort, grouping and folds.
public sealed partial class ProjectsViewModel
{
    private readonly HashSet<string> expanded = [];

    /// <summary>How the open project was left.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortText), nameof(GroupText), nameof(CanReorder), nameof(IsSorted), nameof(IsGrouped))]
    public partial ProjectLayout Layout { get; private set; } = new();

    public string SortText => Layout.Sort == TaskSort.None ? "Sort" : $"Sort: {ProjectList.Label(Layout.Sort)}";
    public string GroupText => Layout.Group == TaskGroup.Sections ? "Group" : $"Group: {ProjectList.Label(Layout.Group)}";
    public bool IsSorted => Layout.Sort != TaskSort.None;
    public bool IsGrouped => Layout.Group != TaskGroup.Sections;

    /// <summary>Tasks drag into order only while the list isn't sorted or grouped by something else, as in Asana.</summary>
    public bool CanReorder => ProjectList.CanReorder(Layout);

    public static IReadOnlyList<TaskSort> SortChoices { get; } = Enum.GetValues<TaskSort>();
    public static IReadOnlyList<TaskGroup> GroupChoices { get; } = Enum.GetValues<TaskGroup>();

    private ProjectLayout LayoutOf(string projectId) =>
        services.Settings.ProjectLayouts.TryGetValue(projectId, out var layout) ? layout : new ProjectLayout { Board = services.Settings.ProjectView.Board };

    private void SaveLayout(ProjectLayout layout)
    {
        Layout = layout;
        if (open is null)
            return;
        var all = services.Settings.ProjectLayouts.ToDictionary(p => p.Key, p => p.Value);
        all[open.Id] = layout;
        services.Settings.ProjectLayouts = all;
    }

    /// <summary>Keeps a project's view from the start, as a board template opening on its board.</summary>
    internal void StartLayout(string projectId, ProjectLayout layout)
    {
        var all = services.Settings.ProjectLayouts.ToDictionary(p => p.Key, p => p.Value);
        all[projectId] = layout;
        services.Settings.ProjectLayouts = all;
    }

    [RelayCommand]
    private void SortBy(TaskSort sort)
    {
        SaveLayout(Layout with { Sort = sort });
        ShowTasks();
    }

    [RelayCommand]
    private void GroupBy(TaskGroup group)
    {
        SaveLayout(Layout with { Group = group });
        ShowTasks();
    }

    /// <summary>Folds a heading up to its name, or opens it out; kept for next time.</summary>
    public void Fold(SectionRow section)
    {
        SaveLayout(ProjectList.Fold(Layout, section.Group.Key, !section.IsFolded));
        ShowTasks();
    }

    /// <summary>Shows a task's subtasks under it in the list, or hides them again.</summary>
    public void Expand(TaskRow row)
    {
        if (!expanded.Remove(row.Task.Id))
            expanded.Add(row.Task.Id);
        ShowTasks();
    }
}
