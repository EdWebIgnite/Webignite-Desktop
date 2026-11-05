using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Projects;
using Webignite.Services;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Projects page: shared and personal projects on the left, the open one on the right with its headings and
/// tasks. Tasks are given to someone, dated and ticked off in the list, and dragged to a new place or heading.
/// What's typed in a name or title is saved a moment after typing stops. Nothing syncs yet.
/// </summary>
public sealed partial class ProjectsViewModel : ViewModelBase, IRefreshable, IHoldsWork, ITabTitle, IHasPanes, IDisposable
{
    private readonly AppServices services;
    private readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly HashSet<object> typed = [];
    private Project? open;
    private bool ours;
    private bool loading;

    public ProjectsViewModel(AppServices services)
    {
        this.services = services;
        Panels = new PanelsViewModel(services.Settings, "projects", [("list", "Projects"), ("project", "Open project"), ("task", "Open task")]);
        typing.Tick += (_, _) => SaveTyped();
        services.Projects.Changed += ElsewhereChanged;
        IsBoard = services.Settings.ProjectView.Board;
        // The task pane shows when a task is opened.
        if (Panels.CanHide("task"))
            Panels["task"].IsShown = false;
        Refresh();
    }

    public PanelsViewModel Panels { get; }

    public string? TabTitle => IsMyTasks ? "My tasks" : IsTemplates ? "Templates" : open is null ? null : EditName.Trim().Length > 0 ? EditName.Trim() : "Project";

    public const string ShowingMine = "#mine";
    public const string ShowingTemplates = "#templates";

    /// <summary>
    /// What is on the right now: My tasks, the templates, a project's id, or null for nothing. The sidebar's
    /// Projects group marks the matching entry.
    /// </summary>
    public string? Showing => IsMyTasks ? ShowingMine : IsTemplates ? ShowingTemplates : open?.Id;

    public bool HoldsWork => open is not null || Detail is not null || typed.Count > 0;

    // The list on the left

    public ObservableCollection<ProjectRow> SharedProjects { get; } = [];
    public ObservableCollection<ProjectRow> PersonalProjects { get; } = [];

    [ObservableProperty] public partial bool ShowDone { get; set; }
    [ObservableProperty] public partial string NewProjectName { get; set; } = "";
    [ObservableProperty] public partial string ListMessage { get; private set; } = "";

    public bool IsEmpty => SharedProjects.Count + PersonalProjects.Count == 0 && !services.Projects.Projects().Any();
    public bool HasShared => SharedProjects.Count > 0;
    public bool HasPersonal => PersonalProjects.Count > 0;

    partial void OnShowDoneChanged(bool value) => ShowList();

    public void Refresh()
    {
        LoadChoices();
        ShowList();
        if (open is not null)
        {
            if (services.Projects.Get(open.Id) is { } fresh)
                Show(fresh);
            else
                CloseProject();
        }
        else if (IsMyTasks)
            ShowMyTasks();
        else if (IsTemplates)
            ListTemplates();
        if (Detail is { } detail)
        {
            if (services.Projects.GetTask(detail.Task.Id) is null)
                CloseTask();
            else
                detail.Reload();
        }
    }

    private void ShowList()
    {
        var team = services.Team();
        SharedProjects.Clear();
        PersonalProjects.Clear();
        var clients = services.Clients.List().ToDictionary(c => c.Id, c => c.Name);
        MyCount = services.Projects.Mine(services.Me().Id).Count;
        foreach (var project in ProjectRules.Listed(services.Projects.Projects(), ShowDone))
        {
            var row = new ProjectRow(project, project.ClientId is { } id && clients.TryGetValue(id, out var name) ? name : null) { IsOpen = project.Id == open?.Id };
            (project.Shared ? SharedProjects : PersonalProjects).Add(row);
        }
        SharedHeading = $"Shared with {team.Partner!.Name}";
        OnPropertyChanged(nameof(SharedHeading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasShared));
        OnPropertyChanged(nameof(HasPersonal));
    }

    public string SharedHeading { get; private set; } = "Shared";

    [RelayCommand]
    private void NewProject()
    {
        var name = NewProjectName.Trim().Length > 0 ? NewProjectName.Trim() : "New project";
        var (saved, problems) = Ours(() => services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = name }));
        if (saved is null)
        {
            ListMessage = string.Join(" ", problems);
            return;
        }
        NewProjectName = "";
        ListMessage = "";
        ShowList();
        Show(saved);
    }

    // Templates: a project kept to start others from.

    public IReadOnlyList<ProjectTemplate> Templates => services.Settings.ProjectTemplates;

    /// <summary>The templates as shown in the Templates view, each with a way to start a project or remove it.</summary>
    public ObservableCollection<TemplateRow> TemplateRows { get; } = [];

    public bool TemplatesEmpty => IsTemplates && TemplateRows.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosed), nameof(TabTitle), nameof(Showing), nameof(TemplatesEmpty))]
    public partial bool IsTemplates { get; private set; }

    /// <summary>Shows the templates on the right, to start a project from one or tidy them up.</summary>
    public void ShowTemplates()
    {
        SaveTyped();
        CloseProject();
        IsMyTasks = false;
        IsTemplates = true;
        ListTemplates();
        if (!Panels["project"].IsShown)
            Panels["project"].IsShown = true;
    }

    private void TemplatesChanged()
    {
        OnPropertyChanged(nameof(Templates));
        ListTemplates();
    }

    private void ListTemplates()
    {
        TemplateRows.Clear();
        foreach (var template in Templates.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            TemplateRows.Add(new TemplateRow(template, this));
        OnPropertyChanged(nameof(TemplatesEmpty));
    }

    /// <summary>Keeps the open project as a template, under its name; one of the same name is replaced.</summary>
    [RelayCommand]
    private void SaveAsTemplate()
    {
        if (open is null)
            return;
        SaveTyped();
        var template = services.Projects.TemplateOf(open.Id, open.Name);
        var replacing = Templates.Any(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));
        services.Settings.ProjectTemplates = [.. Templates.Where(t => !string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)), template];
        TemplatesChanged();
        services.ShowNote?.Invoke(replacing ? "Template updated" : "Saved as a template",
            $"{template.Name}: {template.Headings.Count} headings, {template.Tasks.Count} tasks. Start a project from it under Templates in the sidebar.", null);
    }

    /// <summary>A new project from a template, named as typed (or after the template), starting today.</summary>
    public void NewFromTemplate(ProjectTemplate template)
    {
        SaveTyped();
        var saved = Ours(() => services.Projects.AddFromTemplate(template, NewProjectName, DateOnly.FromDateTime(DateTime.Today)));
        NewProjectName = "";
        ListMessage = "";
        ShowList();
        Show(saved);
    }

    public void RemoveTemplate(ProjectTemplate template)
    {
        var before = Templates;
        services.Settings.ProjectTemplates = [.. before.Where(t => t.Id != template.Id)];
        TemplatesChanged();
        services.OfferUndo($"Removed the template {template.Name}", () =>
        {
            services.Settings.ProjectTemplates = before;
            TemplatesChanged();
        });
    }

    [RelayCommand]
    private void Open(ProjectRow row)
    {
        SaveTyped();
        if (services.Projects.Get(row.Project.Id) is { } project)
            Show(project);
    }

    /// <summary>Opens a project by id, e.g. from Ctrl+K or a client's page later.</summary>
    public void OpenProject(string id)
    {
        SaveTyped();
        if (services.Projects.Get(id) is { } project)
            Show(project);
    }

    // The open project

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosed), nameof(TabTitle), nameof(HoldsWork), nameof(Showing))]
    public partial bool IsOpen { get; private set; }

    public bool IsClosed => !IsOpen && !IsMyTasks && !IsTemplates;

    // My tasks: everything given to me across projects, by when it's due.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosed), nameof(TabTitle), nameof(Showing))]
    public partial bool IsMyTasks { get; private set; }

    public ObservableCollection<object> MyRows { get; } = [];

    [ObservableProperty] public partial int MyCount { get; private set; }
    [ObservableProperty] public partial string MySummary { get; private set; } = "";

    public bool MyTasksEmpty => IsMyTasks && MyRows.Count == 0;

    [RelayCommand]
    public void ShowMine()
    {
        SaveTyped();
        CloseProject();
        IsTemplates = false;
        IsMyTasks = true;
        ShowMyTasks();
        if (!Panels["project"].IsShown)
            Panels["project"].IsShown = true;
    }

    private void ShowMyTasks()
    {
        MyRows.Clear();
        var me = services.Me().Id;
        var mine = services.Projects.Mine(me);
        MyCount = mine.Count;
        var names = services.Projects.Projects().ToDictionary(p => p.Id, p => p.Name);
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var (title, tasks) in ProjectRules.ByWhen(mine, today))
        {
            MyRows.Add(new MyGroupRow(title, tasks.Count, title == "Overdue"));
            foreach (var task in tasks)
            {
                var subtasks = task.ParentId is null ? ProjectRules.SubtaskCount(services.Projects.AllTasks(task.ProjectId).Where(t => t.ParentId == task.Id)) : "";
                MyRows.Add(new TaskRow(task, null, subtasks, People, this)
                {
                    ProjectName = names.GetValueOrDefault(task.ProjectId),
                    IsOpen = task.Id == Detail?.Task.Id,
                });
            }
        }
        MySummary = mine.Count switch
        {
            0 => "Nothing given to you right now.",
            1 => "1 task given to you, across your projects.",
            _ => $"{mine.Count} tasks given to you, across your projects.",
        };
        OnPropertyChanged(nameof(MyTasksEmpty));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    public partial string EditName { get; set; } = "";

    [ObservableProperty] public partial bool EditShared { get; set; }
    [ObservableProperty] public partial Choice<Client?>? EditClient { get; private set; }
    [ObservableProperty] public partial Choice<ProjectStatus>? EditStatus { get; private set; }
    [ObservableProperty] public partial DateTime? EditDue { get; set; }
    [ObservableProperty] public partial string NewHeadingName { get; set; } = "";
    [ObservableProperty] public partial string Message { get; private set; } = "";
    [ObservableProperty] public partial string Summary { get; private set; } = "";

    public IReadOnlyList<Choice<Client?>> ClientChoices { get; private set; } = [];
    public IReadOnlyList<Choice<string?>> People { get; private set; } = [];

    public static IReadOnlyList<Choice<ProjectStatus>> Statuses { get; } =
        [.. Enum.GetValues<ProjectStatus>().Select(s => new Choice<ProjectStatus>(ProjectRules.Label(s), s))];

    /// <summary>The headings and tasks as shown: a heading row, then its tasks, then a box to add one.</summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>The same headings and tasks as a board: a column per heading, its tasks as cards.</summary>
    public ObservableCollection<BoardColumn> Columns { get; } = [];

    // Filtering the list and board by priority and tag.

    public static IReadOnlyList<Choice<TaskPriority?>> PriorityFilters { get; } =
    [
        new("Any priority", null), new("High", TaskPriority.High), new("Normal", TaskPriority.Normal), new("Low", TaskPriority.Low),
    ];

    public static IReadOnlyList<Choice<TaskRepeat>> Repeats { get; } =
        [.. Enum.GetValues<TaskRepeat>().Select(r => new Choice<TaskRepeat>(ProjectRules.Label(r), r))];

    public static IReadOnlyList<Choice<TaskPriority>> Priorities { get; } =
        [new("High", TaskPriority.High), new("Normal", TaskPriority.Normal), new("Low", TaskPriority.Low)];

    public IReadOnlyList<Choice<string?>> TagFilters { get; private set; } = [new("Any tag", null)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilter), nameof(FilterText))]
    public partial Choice<TaskPriority?>? FilterPriority { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilter), nameof(FilterText))]
    public partial Choice<string?>? FilterTag { get; private set; }

    public bool HasFilter => FilterPriority?.Value is not null || FilterTag?.Value is not null;

    /// <summary>The filter button's words: "Filter", or what's picked, e.g. "High · design".</summary>
    public string FilterText => HasFilter
        ? string.Join(" · ", new[] { FilterPriority?.Value is null ? null : FilterPriority.Label, FilterTag?.Value }.Where(t => t is not null))
        : "Filter";

    /// <summary>The priority picked in the filter; the list is one-way, so only a real pick counts.</summary>
    public void PickPriorityFilter(Choice<TaskPriority?> choice)
    {
        FilterPriority = choice;
        ShowTasks();
    }

    public void PickTagFilter(Choice<string?> choice)
    {
        FilterTag = choice;
        ShowTasks();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterPriority = PriorityFilters[0];
        FilterTag = TagFilters[0];
        ShowTasks();
    }

    /// <summary>Tags used in the open project, for the task pane to offer.</summary>
    public IReadOnlyList<string> ProjectTags { get; private set; } = [];

    /// <summary>Board or list; remembered for next time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList))]
    public partial bool IsBoard { get; set; }

    public bool IsList => !IsBoard;

    partial void OnIsBoardChanged(bool value) => services.Settings.ProjectView = services.Settings.ProjectView with { Board = value };

    [RelayCommand]
    private void ListView() => IsBoard = false;

    [RelayCommand]
    private void BoardView() => IsBoard = true;

    public string SharedLabel => $"Shared with {services.Team().Partner!.Name}";

    private void LoadChoices()
    {
        var team = services.Team();
        People = [new Choice<string?>("No one", null), new Choice<string?>(team.Me!.Name, team.Me.Id), new Choice<string?>(team.Partner!.Name, team.Partner.Id)];
        ClientChoices = ClientPicker.Choices("No client", services.Clients.List().OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase));
        OnPropertyChanged(nameof(People));
        OnPropertyChanged(nameof(ClientChoices));
        OnPropertyChanged(nameof(SharedLabel));
    }

    private void Show(Project project)
    {
        if (Detail is { } shown && shown.Task.ProjectId != project.Id)
            CloseTask();
        if (open?.Id != project.Id)
        {
            FilterPriority = PriorityFilters[0];
            FilterTag = null;
        }
        open = project;
        IsMyTasks = false;
        IsTemplates = false;
        MyRows.Clear();
        loading = true;
        EditName = project.Name;
        EditShared = project.Shared;
        EditClient = ClientChoices.FirstOrDefault(c => c.Value?.Id == project.ClientId) ?? ClientChoices.FirstOrDefault();
        EditStatus = Statuses.First(s => s.Value == project.Status);
        EditDue = project.Due?.ToDateTime(TimeOnly.MinValue);
        loading = false;
        Message = "";
        IsOpen = true;
        foreach (var row in SharedProjects.Concat(PersonalProjects))
            row.IsOpen = row.Project.Id == project.Id;
        ShowTasks();
        if (!Panels["project"].IsShown)
            Panels["project"].IsShown = true;
        OnPropertyChanged(nameof(Showing));
    }

    private void ShowTasks()
    {
        if (IsMyTasks)
        {
            ShowMyTasks();
            return;
        }
        Rows.Clear();
        Columns.Clear();
        if (open is null)
            return;
        var tasks = services.Projects.AllTasks(open.Id);
        ProjectTags = ProjectRules.AllTags(tasks);
        TagFilters = [new Choice<string?>("Any tag", null), .. ProjectTags.Select(t => new Choice<string?>(t, t))];
        OnPropertyChanged(nameof(TagFilters));
        FilterPriority ??= PriorityFilters[0];
        FilterTag = TagFilters.FirstOrDefault(t => string.Equals(t.Value, FilterTag?.Value, StringComparison.OrdinalIgnoreCase)) ?? TagFilters[0];
        // The lists were rebuilt, so the drop-downs need their picks again.
        OnPropertyChanged(nameof(FilterTag));
        OnPropertyChanged(nameof(FilterPriority));
        var priority = FilterPriority?.Value;
        var tag = FilterTag?.Value;
        var shown = tasks.Where(t => t.ParentId is not null || ProjectRules.Shows(t, priority, tag));
        var sections = ProjectRules.Sections(services.Projects.Headings(open.Id), shown);
        foreach (var (heading, list) in sections)
        {
            var section = new SectionRow(heading, list, this);
            Rows.Add(section);
            var cards = new List<TaskRow>();
            foreach (var task in list)
            {
                var row = new TaskRow(task, section, ProjectRules.SubtaskCount(tasks.Where(t => t.ParentId == task.Id)), People, this) { IsOpen = task.Id == Detail?.Task.Id };
                Rows.Add(row);
                cards.Add(row);
            }
            var add = new AddTaskRow(section, this);
            Rows.Add(add);
            // The same rows make the board: a column per heading.
            Columns.Add(new BoardColumn(section, cards, add));
        }
        Summary = SummaryOf(tasks);
        if (HasFilter)
            Summary += $". Showing {sections.Sum(x => x.Tasks.Count)} that match.";
    }

    [RelayCommand]
    private void Close()
    {
        SaveTyped();
        CloseProject();
    }

    private void CloseProject()
    {
        if (Detail is not null)
            CloseTask();
        open = null;
        IsOpen = false;
        Rows.Clear();
        OnPropertyChanged(nameof(Showing));
        foreach (var row in SharedProjects.Concat(PersonalProjects))
            row.IsOpen = false;
    }

    partial void OnEditNameChanged(string value) => Typed(this);
    partial void OnEditSharedChanged(bool value) => ChangeProject(p => p with { Shared = value });
    partial void OnEditDueChanged(DateTime? value) => ChangeProject(p => p with { Due = value is { } d ? DateOnly.FromDateTime(d) : null });

    /// <summary>The client picked in the list; the list is one-way, so only a real pick counts.</summary>
    public void PickClient(Choice<Client?> choice)
    {
        EditClient = choice;
        ChangeProject(p => p with { ClientId = choice.Value?.Id });
    }

    public void PickStatus(Choice<ProjectStatus> choice)
    {
        EditStatus = choice;
        ChangeProject(p => p with { Status = choice.Value });
    }

    private void ChangeProject(Func<Project, Project> change)
    {
        if (loading || open is null)
            return;
        var (saved, problems) = Ours(() => services.Projects.Change(open.Id, change));
        if (saved is null)
        {
            Message = string.Join(" ", problems);
            return;
        }
        open = saved;
        Message = "";
        ShowList();
    }

    [RelayCommand]
    private void DeleteProject()
    {
        if (open is null)
            return;
        SaveTyped();
        var name = open.Name;
        var gone = Ours(() => services.Projects.Delete(open.Id));
        CloseProject();
        ShowList();
        if (gone is not null)
            services.OfferUndo($"Deleted {name}", () =>
            {
                services.Projects.Restore(gone);
                Refresh();
            });
    }

    // Headings

    [RelayCommand]
    private void AddHeading()
    {
        if (open is null || NewHeadingName.Trim().Length == 0)
            return;
        var (saved, problems) = Ours(() => services.Projects.AddHeading(open.Id, NewHeadingName));
        Message = saved is null ? string.Join(" ", problems) : "";
        if (saved is null)
            return;
        NewHeadingName = "";
        ShowTasks();
    }

    internal void DeleteHeading(SectionRow section)
    {
        if (section.Heading is not { } heading)
            return;
        SaveTyped();
        var gone = Ours(() => services.Projects.DeleteHeading(heading.Id));
        ShowTasks();
        if (gone is not null)
            services.OfferUndo($"Deleted the heading {heading.Name}. Its tasks are under no heading now.", () =>
            {
                services.Projects.Restore(gone);
                Refresh();
            });
    }

    // Tasks

    internal void AddTask(SectionRow section, string title)
    {
        if (open is null || title.Trim().Length == 0)
            return;
        var (saved, problems) = Ours(() => services.Projects.AddTask(ProjectRules.Filtered(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = open.Id, HeadingId = section.Heading?.Id, Title = title,
        }, FilterPriority?.Value, FilterTag?.Value)));
        Message = saved is null ? string.Join(" ", problems) : "";
        if (saved is not null)
            ShowTasks();
    }

    internal void ChangeTask(TaskRow row, Func<ProjectTask, ProjectTask> change)
    {
        var (saved, problems) = Ours(() => services.Projects.ChangeTask(row.Task.Id, change));
        if (saved is null)
        {
            Message = string.Join(" ", problems);
            row.Reset();
            return;
        }
        Message = "";
        var wasDone = row.Task.Done;
        row.Task = saved;
        if (open is { } project)
            Summary = SummaryOf(services.Projects.AllTasks(project.Id));
        // A repeating task ticked off has made its next one.
        if (!wasDone && saved.Done && saved.Repeat != TaskRepeat.None)
        {
            ShowTasks();
            if (Detail is { } shown && shown.Task.Id == saved.Id)
                shown.Reload();
            return;
        }
        // A subtask ticked in the task pane changes its task's count in the list; a task changed in the list
        // shows in the task pane if it's open there.
        if (saved.ParentId is not null)
            ShowTasks();
        else if (Detail is { } detail && detail.Task.Id == saved.Id)
            detail.Reload();
    }

    private static string SummaryOf(IReadOnlyList<ProjectTask> tasks)
    {
        var top = tasks.Where(t => t.ParentId is null).ToList();
        return top.Count == 0 ? "No tasks yet" : $"{top.Count(t => t.Done)} of {top.Count} tasks done";
    }

    internal void DeleteTask(TaskRow row)
    {
        SaveTyped();
        var gone = Ours(() => services.Projects.DeleteTask(row.Task.Id));
        if (Detail is { } detail)
        {
            if (detail.Task.Id == row.Task.Id)
                CloseTask();
            else
                detail.LoadSubtasks();
        }
        ShowTasks();
        if (gone is not null)
            services.OfferUndo($"Deleted {row.Task.Title}", () =>
            {
                services.Projects.Restore(gone);
                Refresh();
            });
    }

    /// <summary>The headings a task can move to, for its right-click menu.</summary>
    public IReadOnlyList<SectionRow> Sections => [.. Rows.OfType<SectionRow>()];

    /// <summary>
    /// A task dragged onto another task (above or below it) or onto a heading (to the top of its tasks), or
    /// moved with its menu (to the end of a heading).
    /// </summary>
    public void Drop(TaskRow dragged, object target, bool below)
    {
        SaveTyped();
        var (section, index) = target switch
        {
            TaskRow { Section: { } under } task => (under, under.Tasks.ToList().FindIndex(t => t.Id == task.Task.Id) + (below ? 1 : 0)),
            SectionRow heading => (heading, 0),
            AddTaskRow add => (add.Section, add.Section.Tasks.Count),
            BoardColumn column => (column.Section, column.Section.Tasks.Count),
            _ => (null, 0),
        };
        if (section is not null)
            Place(dragged, section, index);
    }

    /// <summary>Moves a task to the end of a heading, from its right-click menu.</summary>
    public void MoveTo(TaskRow row, SectionRow section)
    {
        SaveTyped();
        Place(row, section, section.Tasks.Count);
    }

    private void Place(TaskRow dragged, SectionRow section, int index)
    {
        var order = ProjectRules.PlaceAt(section.Tasks, index, dragged.Task.Id);
        Ours(() => services.Projects.ChangeTask(dragged.Task.Id, t => t with { HeadingId = section.Heading?.Id, Order = order }));
        ShowTasks();
    }


    // Typing: names and titles are saved a moment after typing stops.

    internal void Typed(object what)
    {
        if (loading)
            return;
        typed.Add(what);
        typing.Stop();
        typing.Start();
        OnPropertyChanged(nameof(HoldsWork));
    }

    /// <summary>Saves anything still being typed, e.g. before another project opens or the app closes.</summary>
    public void Keep() => SaveTyped();

    private void SaveTyped()
    {
        typing.Stop();
        var pending = typed.ToList();
        typed.Clear();
        foreach (var what in pending)
        {
            switch (what)
            {
                case ProjectsViewModel when open is not null:
                    if (EditName.Trim().Length == 0)
                        Message = "Give the project a name.";
                    else
                        ChangeProject(p => p with { Name = EditName });
                    break;
                case TaskRow row when row.Title.Trim().Length > 0:
                    ChangeTask(row, t => t with { Title = row.Title });
                    break;
                case TaskDetail detail when detail == Detail:
                    detail.SaveTyped();
                    break;
                case SectionRow { Heading: { } heading } section when section.Name.Trim().Length > 0:
                    Ours(() => services.Projects.ChangeHeading(heading.Id, h => h with { Name = section.Name }));
                    break;
            }
        }
        OnPropertyChanged(nameof(HoldsWork));
    }

    // Our own changes are shown as they're made; a change from elsewhere (another copy of the page) reloads.
    internal T Ours<T>(Func<T> work)
    {
        ours = true;
        try
        {
            return work();
        }
        finally
        {
            ours = false;
        }
    }

    private void ElsewhereChanged(string? projectId)
    {
        if (ours)
            return;
        if (projectId is null || projectId == open?.Id)
            Refresh();
        else
            ShowList();
    }

    public void Dispose()
    {
        SaveTyped();
        typing.Stop();
        services.Projects.Changed -= ElsewhereChanged;
    }
}

/// <summary>A project in the list on the left.</summary>
public sealed partial class ProjectRow(Project project, string? clientName) : ObservableObject
{
    public Project Project { get; } = project;
    public string Name => Project.Name;
    public string About => string.Join(" · ", new[] { clientName, Project.Status == ProjectStatus.Active ? null : ProjectRules.Label(Project.Status),
        Project.Due is { } due ? "Due " + due.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : null }.Where(t => !string.IsNullOrEmpty(t)));
    public bool HasAbout => About.Length > 0;
    public bool IsDone => Project.Status == ProjectStatus.Done;

    [ObservableProperty] public partial bool IsOpen { get; set; }
}

/// <summary>A heading in the open project (or the tasks under no heading), with its name to change.</summary>
public sealed partial class SectionRow(Heading? heading, IReadOnlyList<ProjectTask> tasks, ProjectsViewModel page) : ObservableObject
{
    public Heading? Heading { get; } = heading;
    public IReadOnlyList<ProjectTask> Tasks { get; } = tasks;
    public bool IsHeading => Heading is not null;
    public bool IsLoose => Heading is null;
    public string Count => Tasks.Count == 0 ? "" : $"{Tasks.Count(t => t.Done)}/{Tasks.Count}";

    [ObservableProperty] public partial string Name { get; set; } = heading?.Name ?? "No heading";
    [ObservableProperty] public partial bool DropHere { get; set; }

    partial void OnNameChanged(string value)
    {
        if (Heading is not null && value != Heading.Name)
            page.Typed(this);
    }

    [RelayCommand]
    private void Delete() => page.DeleteHeading(this);
}

/// <summary>A task in the list: tick, title, who it's for and when it's due, all changed in place.</summary>
public sealed partial class TaskRow : ObservableObject
{
    private readonly ProjectsViewModel page;
    private bool loading;

    public TaskRow(ProjectTask task, SectionRow? section, string subtasks, IReadOnlyList<Choice<string?>> people, ProjectsViewModel page)
    {
        this.page = page;
        Section = section;
        Subtasks = subtasks;
        People = people;
        Task = task;
        Reset();
    }

    /// <summary>Its project, shown in My tasks.</summary>
    public string? ProjectName { get; init; }
    public bool HasProjectName => ProjectName is not null;

    /// <summary>The heading it's under in the list; null for a subtask in the task pane.</summary>
    public SectionRow? Section { get; }
    public string Subtasks { get; }
    public bool HasSubtasks => Subtasks.Length > 0;
    public IReadOnlyList<Choice<string?>> People { get; }

    public ProjectTask Task { get; set; }

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fade))]
    public partial bool Done { get; set; }

    /// <summary>A done task is greyed in the list.</summary>
    public double Fade => Done ? 0.55 : 1;

    public string PriorityLabel => Task.Priority == TaskPriority.Normal ? "" : ProjectRules.Label(Task.Priority);
    public bool IsHigh => Task.Priority == TaskPriority.High;
    public bool IsLow => Task.Priority == TaskPriority.Low;
    public IReadOnlyList<string> Tags => Task.Tags;
    public bool HasMarks => Task.Priority != TaskPriority.Normal || Task.Tags.Count > 0 || Task.Repeat != TaskRepeat.None;
    public bool Repeats => Task.Repeat != TaskRepeat.None;
    public string RepeatLabel => ProjectRules.Label(Task.Repeat);

    /// <summary>The line under a card's title on the board: who it's for, when it's due, its subtasks.</summary>
    public string CardLine => string.Join(" · ", new[]
    {
        AssignedTo?.Value is null ? null : AssignedTo.Label,
        Due is { } due ? "Due " + due.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : null,
        Subtasks.Length > 0 ? Subtasks + " subtasks" : null,
    }.Where(t => t is not null));

    public bool HasCardLine => CardLine.Length > 0;

    /// <summary>Who it's for, as the list shows it: a name you click to change, or "No one".</summary>
    public string WhoText => AssignedTo?.Value is null ? "No one" : AssignedTo.Label;
    public bool HasWho => AssignedTo?.Value is not null;

    /// <summary>When it's due, as the list shows it ("Tomorrow", "12 Nov"), or "Due date" when it has none.</summary>
    public string DueText => Due is { } due ? ProjectRules.DueLabel(DateOnly.FromDateTime(due), DateOnly.FromDateTime(DateTime.Today)) : "Due date";
    public bool HasDue => Due is not null;
    public bool IsLate => Due is { } due && !Done && DateOnly.FromDateTime(due) < DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty] public partial Choice<string?>? AssignedTo { get; set; }
    [ObservableProperty] public partial DateTime? Due { get; set; }
    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial bool DropAbove { get; set; }
    [ObservableProperty] public partial bool DropBelow { get; set; }

    /// <summary>Shows the task as stored, e.g. after a change that wasn't allowed.</summary>
    public void Reset()
    {
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(IsHigh));
        OnPropertyChanged(nameof(IsLow));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasMarks));
        OnPropertyChanged(nameof(Repeats));
        OnPropertyChanged(nameof(RepeatLabel));
        loading = true;
        Title = Task.Title;
        Done = Task.Done;
        AssignedTo = People.FirstOrDefault(p => p.Value == Task.AssignedTo) ?? People.FirstOrDefault();
        Due = Task.Due?.ToDateTime(TimeOnly.MinValue);
        loading = false;
    }

    partial void OnTitleChanged(string value)
    {
        if (!loading)
            page.Typed(this);
    }

    partial void OnDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLate));
        if (!loading)
            page.ChangeTask(this, t => t with { Done = value });
    }

    partial void OnAssignedToChanged(Choice<string?>? value)
    {
        OnPropertyChanged(nameof(WhoText));
        OnPropertyChanged(nameof(HasWho));
        OnPropertyChanged(nameof(CardLine));
        OnPropertyChanged(nameof(HasCardLine));
        if (!loading)
            page.ChangeTask(this, t => t with { AssignedTo = value?.Value });
    }

    partial void OnDueChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(DueText));
        OnPropertyChanged(nameof(HasDue));
        OnPropertyChanged(nameof(IsLate));
        OnPropertyChanged(nameof(CardLine));
        OnPropertyChanged(nameof(HasCardLine));
        if (!loading)
            page.ChangeTask(this, t => t with { Due = value is { } d ? DateOnly.FromDateTime(d) : null });
    }

    [RelayCommand]
    private void Delete() => page.DeleteTask(this);
}

/// <summary>A template in the Templates view: what it holds, start a project from it, or remove it.</summary>
public sealed partial class TemplateRow(ProjectTemplate template, ProjectsViewModel page)
{
    public ProjectTemplate Template { get; } = template;
    public string Name => Template.Name;

    public string About => $"{Count(Template.Headings.Count, "heading")}, {Count(Template.Tasks.Count, "task")}";

    private static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    [RelayCommand]
    private void Start() => page.NewFromTemplate(Template);

    [RelayCommand]
    private void Remove() => page.RemoveTemplate(Template);
}

/// <summary>A group in My tasks: Overdue, Today, This week, Later or No due date.</summary>
public sealed class MyGroupRow(string title, int count, bool overdue)
{
    public string Title { get; } = title;
    public string Count { get; } = count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public bool Overdue { get; } = overdue;
}

/// <summary>A column on the board: a heading, its cards, and a box to add one.</summary>
public sealed partial class BoardColumn(SectionRow section, IReadOnlyList<TaskRow> cards, AddTaskRow add) : ObservableObject
{
    public SectionRow Section { get; } = section;
    public IReadOnlyList<TaskRow> Cards { get; } = cards;
    public AddTaskRow Add { get; } = add;

    [ObservableProperty] public partial bool DropHere { get; set; }

    /// <summary>The column's edge while a card is held over it.</summary>
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Thickness> DropEdge = new(on => on ? new Avalonia.Thickness(2) : default);
}

/// <summary>The box under a heading's tasks for adding one.</summary>
public sealed partial class AddTaskRow(SectionRow section, ProjectsViewModel page) : ObservableObject
{
    public SectionRow Section { get; } = section;

    [ObservableProperty] public partial string Title { get; set; } = "";

    [RelayCommand]
    private void Add()
    {
        var title = Title;
        Title = "";
        page.AddTask(Section, title);
    }
}
