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
    public ProjectOverviewPart OverviewPart { get; }
    public ProjectTimelinePart TimelinePart { get; }
    public ProjectFieldsPart FieldsPart { get; }

    // Each view of a project with state of its own is its own part; the page holds them and the screen binds to them by name.
    public ProjectMessagesPart MessagesPart { get; }
    public ProjectFilesPart FilesPart { get; }
    public ProjectGalleryPart GalleryPart { get; }
    public ProjectMyTodosPart MyTodosPart { get; }
    private readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly HashSet<object> typed = [];
    internal Project? open;
    private bool ours;
    internal bool loading;

    public ProjectsViewModel(AppServices services)
    {
        this.services = services;
        OverviewPart = new(services, this);
        TimelinePart = new(services, this);
        FieldsPart = new(services, this);
        MessagesPart = new(services, this);
        FilesPart = new(services, this);
        GalleryPart = new(services, this);
        MyTodosPart = new(services, this);
        Panels = new PanelsViewModel(services.Settings, "projects", [("list", "Projects"), ("project", "Open project"), ("task", "Open task")]);
        typing.Tick += (_, _) => SaveTyped();
        services.Projects.Changed += ElsewhereChanged;
        Tab = services.Settings.ProjectView.Board ? ProjectTab.Board : ProjectTab.List;
        // The task pane shows when a task is opened.
        if (Panels.CanHide("task"))
            Panels["task"].IsShown = false;
        Refresh();
    }

    public PanelsViewModel Panels { get; }

    /// <summary>
    /// The list of projects tucks away while a project, My tasks or Templates is open, as the Planner sidebar
    /// lists them already; it can be opened again from its edge, and comes back with All projects.
    /// </summary>
    private void TuckList()
    {
        if (Panels["list"].IsShown && Panels.CanHide("list"))
            Panels["list"].IsShown = false;
    }

    /// <summary>All projects: whatever was open closes and the list of projects shows.</summary>
    public void ShowAll()
    {
        SaveTyped();
        CloseProject();
        IsMyTasks = false;
        IsTemplates = false;
        OnPropertyChanged(nameof(Showing));
        OnPropertyChanged(nameof(TabTitle));
        if (!Panels["list"].IsShown)
            Panels["list"].IsShown = true;
    }

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
    [ObservableProperty] public partial string ListMessage { get; internal set; } = "";

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
            GalleryPart.ListTemplates();
        if (Detail is { } detail)
        {
            if (services.Projects.GetTask(detail.Task.Id) is null)
                CloseTask();
            else
                detail.Reload();
        }
        if (IsMessages)
            MessagesPart.ListMessages();
    }

    internal void ShowList()
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosed), nameof(TabTitle), nameof(Showing))]
    public partial bool IsTemplates { get; private set; }

    partial void OnIsTemplatesChanged(bool value) => GalleryPart.GalleryShownChanged();

    /// <summary>Shows the template gallery on the right, to start a project from a template or a blank one.</summary>
    public void ShowTemplates()
    {
        SaveTyped();
        CloseProject();
        IsMyTasks = false;
        IsTemplates = true;
        GalleryPart.Previewing = null;
        GalleryPart.ListTemplates();
        if (!Panels["project"].IsShown)
            Panels["project"].IsShown = true;
        TuckList();
    }

    private void TemplatesChanged()
    {
        OnPropertyChanged(nameof(Templates));
        GalleryPart.ListTemplates();
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
            $"{template.Name}: {template.Headings.Count} headings, {template.Tasks.Count} tasks. It's under My templates in the template gallery.", null);
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
        {
            Show(project);
            TuckList();
        }
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
        TuckList();
    }

    internal void ShowMyTasks()
    {
        MyRows.Clear();
        var me = services.Me().Id;
        var mine = services.Projects.Mine(me);
        var todos = MyTodosPart.MyTodos();
        var openTodos = todos.Count(t => !t.Done);
        MyCount = mine.Count + openTodos;
        var names = services.Projects.Projects().ToDictionary(p => p.Id, p => p.Name);
        var today = DateOnly.FromDateTime(DateTime.Today);
        // Tasks given to me and my own to-dos, together by when they're due, as Asana's My tasks.
        foreach (var (title, items) in MyWork.ByWhen(mine, todos, today, System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek))
        {
            MyRows.Add(new MyGroupRow(title, items.Count, title == "Overdue"));
            foreach (var item in items)
            {
                if (item.Todo is { } todo)
                {
                    MyRows.Add(new TodoRow(todo, this));
                    continue;
                }
                var task = item.Task!;
                var subtasks = task.ParentId is null ? ProjectRules.SubtaskCount(services.Projects.AllTasks(task.ProjectId).Where(t => t.ParentId == task.Id)) : "";
                MyRows.Add(new TaskRow(task, null, subtasks, People, this)
                {
                    ProjectName = names.GetValueOrDefault(task.ProjectId),
                    IsOpen = task.Id == Detail?.Task.Id,
                });
            }
        }
        MySummary = MyWork.Summary(mine.Count, openTodos);
        MyTodosPart.MyMessage = "";
        OnPropertyChanged(nameof(MyTasksEmpty));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    public partial string EditName { get; set; } = "";

    [ObservableProperty] public partial bool EditShared { get; set; }

    // The header, as Asana's: the project's coloured square, a star, and how it's going.

    [ObservableProperty] public partial string EditColour { get; private set; } = "";
    [ObservableProperty] public partial string EditIcon { get; private set; } = "";
    [ObservableProperty] public partial bool EditFavourite { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthText), nameof(HasHealth))]
    public partial ProjectHealth EditHealth { get; internal set; }

    public string HealthText => ProjectLooks.Label(EditHealth);
    public bool HasHealth => EditHealth != ProjectHealth.None;

    public static IReadOnlyList<string> IconChoices => ProjectLooks.Icons;
    public static IReadOnlyList<string> ColourChoices => ProjectLooks.Colours;

    public static IReadOnlyList<ProjectHealth> HealthChoices { get; } = [ProjectHealth.OnTrack, ProjectHealth.AtRisk, ProjectHealth.OffTrack];

    /// <summary>Who's on the project, as the header's round faces: me, and my partner when it's shared.</summary>
    public IReadOnlyList<string> Members => EditShared && services.Team().Partner is { } partner
        ? [services.Me().Name, partner.Name]
        : [services.Me().Name];

    [RelayCommand]
    private void PickColour(string colour)
    {
        EditColour = colour;
        ChangeProject(p => p with { Colour = colour });
    }

    [RelayCommand]
    private void PickIcon(string icon)
    {
        EditIcon = icon;
        ChangeProject(p => p with { Icon = icon });
    }

    [RelayCommand]
    private void ToggleFavourite()
    {
        EditFavourite = !EditFavourite;
        ChangeProject(p => p with { Favourite = EditFavourite });
    }

    [RelayCommand]
    private void SetHealth(ProjectHealth health)
    {
        EditHealth = health == EditHealth ? ProjectHealth.None : health;
        ChangeProject(p => p with { Health = EditHealth });
    }
    [ObservableProperty] public partial Choice<Client?>? EditClient { get; private set; }
    [ObservableProperty] public partial Choice<ProjectStatus>? EditStatus { get; private set; }
    [ObservableProperty] public partial DateTime? EditDue { get; set; }
    [ObservableProperty] public partial string NewHeadingName { get; set; } = "";
    [ObservableProperty] public partial string Message { get; internal set; } = "";
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

    /// <summary>
    /// Which view of the open project is showing. One thing says so, and each view's own flag reads it, so there is
    /// never more than one showing or none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList), nameof(IsBoard), nameof(IsFiles), nameof(IsOverview), nameof(IsTimeline), nameof(IsCalendar),
        nameof(IsMessages), nameof(NoProjectFiles), nameof(ShowsTaskTools), nameof(ShowsSortGroup))]
    public partial ProjectTab Tab { get; private set; }

    public bool IsList => Tab == ProjectTab.List;

    /// <summary>The board is showing, rather than the list.</summary>
    public bool IsBoard => Tab == ProjectTab.Board;

    /// <summary>The Files tab is showing, rather than the list or the board.</summary>
    public bool IsFiles => Tab == ProjectTab.Files;

    public bool NoProjectFiles => IsFiles && FilesPart.ProjectFiles.Count == 0;

    /// <summary>The Messages tab is showing.</summary>
    public bool IsMessages => Tab == ProjectTab.Messages;

    /// <summary>The Overview tab is showing.</summary>
    public bool IsOverview => Tab == ProjectTab.Overview;

    /// <summary>Add task, filter, sort and group belong to the tabs that show tasks.</summary>
    public bool ShowsTaskTools => !IsOverview && !IsMessages;

    [RelayCommand]
    private void FilesView() => ShowOnly(ProjectTab.Files);

    [RelayCommand]
    private void MessagesView() => ShowOnly(ProjectTab.Messages);

    [RelayCommand]
    private void OverviewView() => ShowOnly(ProjectTab.Overview);

    public bool IsTimeline => Tab == ProjectTab.Timeline;

    public bool IsCalendar => Tab == ProjectTab.Calendar;

    /// <summary>Sort and Group belong to the list and the board; Timeline and Calendar lay tasks out by date.</summary>
    public bool ShowsSortGroup => !IsTimeline && !IsCalendar;

    [RelayCommand]
    private void TimelineView() => ShowOnly(ProjectTab.Timeline);

    [RelayCommand]
    private void CalendarView() => ShowOnly(ProjectTab.Calendar);

    private static ProjectTab TabOf(ProjectLayout layout) =>
        layout.Overview ? ProjectTab.Overview : layout.Messages ? ProjectTab.Messages : layout.Timeline ? ProjectTab.Timeline : layout.Calendar ? ProjectTab.Calendar
        : layout.Files ? ProjectTab.Files : layout.Board ? ProjectTab.Board : ProjectTab.List;

    // A view that lists things of its own reads them as it is shown. Which view it is stays with the open project
    // only: another project opens the way it was left, a new one as a list.
    partial void OnTabChanged(ProjectTab value)
    {
        switch (value)
        {
            case ProjectTab.Files:
                FilesPart.ListFiles();
                break;
            case ProjectTab.Messages:
                MessagesPart.ListMessages();
                break;
            case ProjectTab.Overview:
                OverviewPart.LoadOverview();
                break;
        }
        if (open is null)
            return;
        var layout = Layout with
        {
            Board = value == ProjectTab.Board, Files = value == ProjectTab.Files, Overview = value == ProjectTab.Overview,
            Timeline = value == ProjectTab.Timeline, Calendar = value == ProjectTab.Calendar, Messages = value == ProjectTab.Messages,
        };
        if (layout != Layout)
            SaveLayout(layout);
    }

    /// <summary>Shows one view; one that lays tasks out by date is laid out again each time it is asked for.</summary>
    private void ShowOnly(ProjectTab tab)
    {
        Tab = tab;
        if (tab is ProjectTab.Timeline or ProjectTab.Calendar && open is not null)
            ShowTasks();
    }

    [RelayCommand]
    private void ListView() => ShowOnly(ProjectTab.List);

    [RelayCommand]
    private void BoardView() => ShowOnly(ProjectTab.Board);

    public string OpenProjectName => open?.Name ?? "";

    public string SharedLabel => $"Shared with {services.Team().Partner!.Name}";

    private void LoadChoices()
    {
        var team = services.Team();
        People = [new Choice<string?>("No one", null), new Choice<string?>(team.Me!.Name, team.Me.Id), new Choice<string?>(team.Partner!.Name, team.Partner.Id)];
        ClientChoices = ClientPicker.Choices("No client", services.Clients.List().OrderBy(c => c.Called, StringComparer.CurrentCultureIgnoreCase));
        OnPropertyChanged(nameof(People));
        OnPropertyChanged(nameof(ClientChoices));
        OnPropertyChanged(nameof(SharedLabel));
    }

    internal void Show(Project project)
    {
        if (Detail is { } shown && shown.Task.ProjectId != project.Id)
            CloseTask();
        if (open?.Id != project.Id)
        {
            FilterPriority = PriorityFilters[0];
            FilterTag = null;
            picked.Clear();
            pickAnchor = null;
        }
        open = project;
        Layout = LayoutOf(project.Id);
        ShowOnly(TabOf(Layout));
        IsMyTasks = false;
        IsTemplates = false;
        MyRows.Clear();
        loading = true;
        EditName = project.Name;
        EditShared = project.Shared;
        EditColour = ProjectLooks.ColourOf(project);
        EditIcon = ProjectLooks.IconOf(project);
        EditFavourite = project.Favourite;
        EditHealth = project.Health;
        EditClient = ClientChoices.FirstOrDefault(c => c.Value?.Id == project.ClientId) ?? ClientChoices.FirstOrDefault();
        EditStatus = Statuses.First(s => s.Value == project.Status);
        EditDue = project.Due?.ToDateTime(TimeOnly.MinValue);
        loading = false;
        Message = "";
        IsOpen = true;
        LoadProjectFolder();
        foreach (var row in SharedProjects.Concat(PersonalProjects))
            row.IsOpen = row.Project.Id == project.Id;
        ShowTasks();
        if (!Panels["project"].IsShown)
            Panels["project"].IsShown = true;
        OnPropertyChanged(nameof(Showing));
    }

    internal void ShowTasks()
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
        var shown = tasks.Where(t => t.ParentId is not null || ProjectRules.Shows(t, priority, tag)).ToList();
        var headings = services.Projects.Headings(open.Id);
        var today = DateOnly.FromDateTime(DateTime.Today);
        string? NameOf(string? id) => id is null ? null : People.FirstOrDefault(p => p.Value == id)?.Label;
        TaskRow Row(ProjectTask task, SectionRow section) => new(task, section, ProjectRules.SubtaskCount(tasks.Where(t => t.ParentId == task.Id)), People, this)
        {
            IsOpen = task.Id == Detail?.Task.Id,
            IsPicked = picked.Contains(task.Id),
            IsExpanded = expanded.Contains(task.Id),
            CommentCount = commentCounts.GetValueOrDefault(task.Id),
            WaitingTip = TaskDependencies.WaitingOn(task, tasks),
            FieldCells = FieldsPart.CellsFor(task),
        };
        FieldsPart.LoadFieldHeaders();
        commentCounts = services.Projects.CommentCounts();
        // The board is always the project's headings; the list is grouped as picked, the same rows when by headings.
        var sections = ProjectList.Arrange(headings, shown, Layout with { Group = TaskGroup.Sections }, NameOf, today, System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        sectionRows = [.. sections.Select(g => new SectionRow(g, this) { IsFolded = Layout.Folded.Contains(g.Key) })];
        var rowsOf = new Dictionary<SectionRow, List<TaskRow>>();
        foreach (var section in sectionRows)
        {
            var cards = section.Tasks.Select(t => Row(t, section)).ToList();
            rowsOf[section] = cards;
            var add = new AddTaskRow(section, this);
            if (typingCard is { } typing && typing.Key == section.Group.Key)
            {
                add.AtTop = typing.Top;
                add.IsTyping = true;
            }
            Columns.Add(new BoardColumn(section, cards, add));
        }
        var listSections = Layout.Group == TaskGroup.Sections ? sectionRows
            : [.. ProjectList.Arrange(headings, shown, Layout, NameOf, today, System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek).Select(g => new SectionRow(g, this) { IsFolded = Layout.Folded.Contains(g.Key) })];
        foreach (var section in listSections)
        {
            Rows.Add(section);
            if (section.IsFolded)
                continue;
            var rows = rowsOf.TryGetValue(section, out var made) ? made : [.. section.Tasks.Select(t => Row(t, section))];
            foreach (var row in rows)
            {
                Rows.Add(row);
                // A task opened out shows its subtasks under it, set in, as in Asana.
                if (row.IsExpanded)
                {
                    foreach (var sub in tasks.Where(t => t.ParentId == row.Task.Id))
                        Rows.Add(new TaskRow(sub, section, "", People, this) { IsOpen = sub.Id == Detail?.Task.Id });
                }
            }
            if (section.IsSection)
                Rows.Add(Columns.FirstOrDefault(c => c.Section == section)?.Add ?? new AddTaskRow(section, this));
        }
        OnPropertyChanged(nameof(CanReorder));
        OverviewPart.LoadOverview();
        TaskRow Plain(ProjectTask task) => new(task, null, ProjectRules.SubtaskCount(tasks.Where(t => t.ParentId == task.Id)), People, this)
        {
            IsOpen = task.Id == Detail?.Task.Id,
            CommentCount = commentCounts.GetValueOrDefault(task.Id),
            WaitingTip = TaskDependencies.WaitingOn(task, tasks),
            FieldCells = FieldsPart.CellsFor(task),
        };
        TimelinePart.LoadTimeline(shown, headings, Plain);
        TimelinePart.LoadCalendar(shown, Plain);
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
    partial void OnEditSharedChanged(bool value)
    {
        OnPropertyChanged(nameof(Members));
        ChangeProject(p => p with { Shared = value });
    }
    partial void OnEditDueChanged(DateTime? value) => ChangeProject(p => p with { Due = value is { } d ? DateOnly.FromDateTime(d) : null });

    /// <summary>The client picked in the list; the list is one-way, so only a real pick counts.</summary>
    public void PickClient(Choice<Client?> choice)
    {
        EditClient = choice;
        var before = open?.ClientId;
        ChangeProject(p => p with { ClientId = choice.Value?.Id });
        if (open is { ClientId: not null } project && project.ClientId != before)
            Files.FolderOffers.Offer(services, project, () => FolderMadeFor(project.Id));
        LoadProjectFolder();
    }

    public void PickStatus(Choice<ProjectStatus> choice)
    {
        EditStatus = choice;
        ChangeProject(p => p with { Status = choice.Value });
    }

    internal void ChangeProject(Func<Project, Project> change)
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

    internal void AddTask(SectionRow section, string title, bool first = false, bool keepTyping = false)
    {
        if (open is null || title.Trim().Length == 0)
            return;
        var (saved, problems) = Ours(() => services.Projects.AddTask(ProjectRules.Filtered(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = open.Id, HeadingId = section.Heading?.Id, Title = title,
        }, FilterPriority?.Value, FilterTag?.Value), first));
        Message = saved is null ? string.Join(" ", problems) : "";
        if (saved is null)
            return;
        // On the board the box stays open for the next card, as Asana's does.
        typingCard = keepTyping ? (section.Group.Key, first) : null;
        ShowTasks();
        typingCard = null;
    }

    /// <summary>The board column whose add box stays open after a card is added, and whether at its top.</summary>
    private (string Key, bool Top)? typingCard;

    /// <summary>The Add heading column at the end of the board is being typed in.</summary>
    [ObservableProperty] public partial bool IsAddingColumn { get; set; }

    internal void ChangeTask(TaskRow row, Func<ProjectTask, ProjectTask> change)
    {
        var beforeTick = BeforeTick(row.Task);
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
        if (IsOverview)
            OverviewPart.LoadOverview();
        // A repeating task ticked off stays the one task: done for now, with the day it comes back.
        if (beforeTick.Count > 0 && !wasDone && saved.BackOn is not null)
        {
            ShowTasks();
            if (Detail is { } shown && shown.Task.Id == saved.Id)
                shown.Reload();
            SayDoneForNow(beforeTick, saved);
            return;
        }
        // A subtask ticked in the task pane changes its task's count in the list; a task changed in the list
        // shows in the task pane if it's open there.
        if (saved.ParentId is not null)
        {
            ShowTasks();
            Detail?.CountSubtasks();
        }
        else if (Detail is { } detail && detail.Task.Id == saved.Id)
            detail.Reload();
    }

    /// <summary>A repeating task and its subtasks as they stand before a tick, to put back if the tick is undone; empty for any other task.</summary>
    internal IReadOnlyList<ProjectTask> BeforeTick(ProjectTask task) =>
        task.Repeat == TaskRepeat.None || task.Done || task.ParentId is not null
            ? []
            : [task, .. services.Projects.AllTasks(task.ProjectId).Where(t => t.ParentId == task.Id)];

    /// <summary>
    /// Says what ticking a repeating task did: it is done for this time and comes back onto the lists on a day
    /// shortly before it is next due, with a way back. Undo puts the task and its subtasks back as they were; the
    /// line in its history stays.
    /// </summary>
    internal void SayDoneForNow(IReadOnlyList<ProjectTask> before, ProjectTask saved)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var doneFor = before[0].Due is { } was ? $" for {was.ToString("d MMMM", culture)}" : "";
        var back = saved.BackOn is { } on ? $" Back on {on.ToString("d MMMM", culture)}." : "";
        services.OfferUndo($"Done{doneFor}.{back}", () =>
        {
            Ours(() =>
            {
                foreach (var old in before)
                    services.Projects.ChangeTask(old.Id, t => t with { Done = old.Done, Due = old.Due, Start = old.Start, RepeatFrom = old.RepeatFrom, BackOn = old.BackOn });
                return true;
            });
            ShowTasks();
            Detail?.Reload();
        });
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
    private IReadOnlyDictionary<string, int> commentCounts = new Dictionary<string, int>();

    /// <summary>A comment was added, changed or deleted: the speech bubbles in the list are counted again.</summary>
    internal void CommentsChanged() => ShowTasks();

    /// <summary>The project's headings (and the tasks under none) as the board has them, whatever the list is grouped by.</summary>
    public IReadOnlyList<SectionRow> Sections => sectionRows;

    private IReadOnlyList<SectionRow> sectionRows = [];

    /// <summary>
    /// A task dragged onto another task (above or below it), onto a heading (to the top of its tasks), to the end
    /// of a heading, or into a board column. When it's one of several picked, they all go, in their order.
    /// </summary>
    public void Drop(TaskRow dragged, object target, bool below)
    {
        SaveTyped();
        var ids = Moving(dragged).Select(t => t.Id).ToList();
        if (target is TaskRow { Task.Id: var on } && ids.Contains(on))
            return;
        var (section, index) = Spot(target, below);
        if (section is not null)
            Place(ids, section.Heading?.Id, index);
    }

    /// <summary>Moves a task to the end of a heading, from its right-click menu.</summary>
    public void MoveTo(TaskRow row, SectionRow section)
    {
        SaveTyped();
        Place(Moving(row).Select(t => t.Id).ToList(), section.Heading?.Id, section.Tasks.Count);
    }

    // Which heading and which place among its tasks a drop on something means.
    private static (SectionRow? Section, int Index) Spot(object target, bool below) => target switch
    {
        TaskRow { Section: { } under } task => (under, under.Tasks.ToList().FindIndex(t => t.Id == task.Task.Id) + (below ? 1 : 0)),
        SectionRow heading => (heading, 0),
        AddTaskRow add => (add.Section, add.Section.Tasks.Count),
        BoardColumn column => (column.Section, column.Section.Tasks.Count),
        _ => (null, 0),
    };

    // Puts each task in turn at the place, the next just after the one before. A subtask put here becomes a task.
    private void Place(IReadOnlyList<string> ids, string? headingId, int index)
    {
        foreach (var id in ids)
        {
            if (Sections.FirstOrDefault(s => s.Heading?.Id == headingId) is not { } section)
                break;
            var order = ProjectRules.PlaceAt(section.Tasks, index, id);
            Ours(() => services.Projects.ChangeTask(id, t => t with { HeadingId = headingId, ParentId = null, Order = order }));
            ShowTasks();
            index = (Sections.FirstOrDefault(s => s.Heading?.Id == headingId)?.Tasks.ToList().FindIndex(t => t.Id == id) ?? index) + 1;
        }
        Detail?.LoadSubtasks();
    }

    // Headings, dragged in the list or as board columns: they share one order.

    /// <summary>Where a dragged heading would land: before which heading, or last (null). Not before the tasks under no heading.</summary>
    private (bool Ok, string? Before) HeadingSpot(object target, bool below)
    {
        var sections = Sections;
        switch (target)
        {
            case SectionRow { Heading: { } heading } when !below:
                return (true, heading.Id);
            case AddTaskRow add when below && sections.Count > 0 && add.Section == sections[^1]:
                return (true, null);
            case BoardColumn column:
                var at = Columns.IndexOf(column);
                if (!below)
                    return column.Section.Heading is { } h ? (true, h.Id) : (false, null);
                return (true, at + 1 < Columns.Count ? Columns[at + 1].Section.Heading?.Id : null);
            default:
                return (false, null);
        }
    }

    public bool CanMoveHeading(SectionRow moving, object target, bool below) =>
        open is not null && moving.Heading is { } heading && HeadingSpot(target, below) is (true, var before)
        && ProjectRules.PlaceHeading(services.Projects.Headings(open.Id), heading.Id, before) is not null;

    /// <summary>Drops a dragged heading, with its tasks, before another or last.</summary>
    public void MoveHeading(SectionRow moving, object target, bool below)
    {
        if (!CanMoveHeading(moving, target, below) || HeadingSpot(target, below) is not (true, var before)
            || ProjectRules.PlaceHeading(services.Projects.Headings(open!.Id), moving.Heading!.Id, before) is not { } order)
            return;
        SaveTyped();
        Ours(() => services.Projects.ChangeHeading(moving.Heading.Id, h => h with { Order = order }));
        ShowTasks();
    }

    // Subtasks, in the open task's pane: dragged into order, dragged out to the list to become tasks, and tasks
    // dragged in from the list to become its subtasks, as in Asana.

    /// <summary>Drops a subtask above or below another of the same task.</summary>
    public void MoveSubtask(TaskRow sub, TaskRow target, bool below)
    {
        if (Detail is not { } detail || sub == target)
            return;
        SaveTyped();
        var list = detail.Subtasks.Select(r => r.Task).ToList();
        var order = ProjectRules.PlaceAt(list, detail.Subtasks.IndexOf(target) + (below ? 1 : 0), sub.Task.Id);
        Ours(() => services.Projects.ChangeTask(sub.Task.Id, t => t with { Order = order }));
        detail.LoadSubtasks();
    }

    /// <summary>A task from the list can become a subtask of the open task, when it's in the same project and isn't that task.</summary>
    public bool TakesAsSubtask(object dragged) =>
        dragged is TaskRow { Section: not null } row && Detail is { } detail && row.Task.ProjectId == detail.Task.ProjectId
        && Moving(row).All(t => t.Id != detail.Task.Id);

    /// <summary>Makes tasks dragged from the list into the open task's subtasks, last among them.</summary>
    public void MakeSubtasks(TaskRow row)
    {
        if (!TakesAsSubtask(row) || Detail is not { } detail)
            return;
        SaveTyped();
        foreach (var task in Moving(row))
        {
            var order = ProjectRules.NextOrder(detail.Subtasks.Select(r => r.Task.Order));
            var (saved, problems) = Ours(() => services.Projects.ChangeTask(task.Id, t => t with { ParentId = detail.Task.Id, HeadingId = detail.Task.HeadingId, Order = order }));
            if (saved is null)
                Message = $"{task.Title}: {string.Join(" ", problems)}";
            detail.LoadSubtasks();
        }
        picked.Clear();
        ShowTasks();
    }

    /// <summary>A subtask from the open task's pane can be dropped on the list, where it becomes a task.</summary>
    public static bool TakesSubtaskBack(object dragged, object target) =>
        dragged is TaskRow { Section: null, Task.ParentId: not null } && target is TaskRow { Section: not null } or SectionRow or AddTaskRow or BoardColumn;

    /// <summary>Drops a subtask on the list: it becomes a task, just above the task it's dropped on, or in that heading.</summary>
    public void Unnest(TaskRow sub, object target)
    {
        if (!TakesSubtaskBack(sub, target) || Spot(target, below: false) is not ({ } section, var index))
            return;
        SaveTyped();
        Place([sub.Task.Id], section.Heading?.Id, index);
    }

    // Picking several tasks, as in Asana: Ctrl+click adds or takes one away, Shift+click picks a run. Dragging
    // one of them takes them all; a plain click on another task lets them go.

    private readonly HashSet<string> picked = [];
    private string? pickAnchor;

    public int PickedCount => picked.Count;

    public void Pick(TaskRow row, bool run)
    {
        var list = Rows.OfType<TaskRow>().ToList();
        var anchor = pickAnchor is null ? -1 : list.FindIndex(r => r.Task.Id == pickAnchor);
        var at = list.FindIndex(r => r.Task.Id == row.Task.Id);
        if (run && anchor >= 0 && at >= 0)
        {
            foreach (var r in list.Skip(Math.Min(anchor, at)).Take(Math.Abs(anchor - at) + 1))
                picked.Add(r.Task.Id);
        }
        else
        {
            if (!picked.Remove(row.Task.Id))
                picked.Add(row.Task.Id);
            pickAnchor = row.Task.Id;
        }
        ShowPicks();
    }

    public void ClearPicks()
    {
        if (picked.Count == 0)
            return;
        picked.Clear();
        pickAnchor = null;
        ShowPicks();
    }

    private void ShowPicks()
    {
        foreach (var row in Rows.OfType<TaskRow>().Concat(Columns.SelectMany(c => c.Cards)))
            row.IsPicked = picked.Contains(row.Task.Id);
        OnPropertyChanged(nameof(PickedCount));
    }

    /// <summary>What a drag of this row takes: every picked task, in the list's order, when it's one of them; otherwise just it.</summary>
    public IReadOnlyList<ProjectTask> Moving(TaskRow row) =>
        row.IsPicked && picked.Count > 1 ? [.. Rows.OfType<TaskRow>().Where(r => picked.Contains(r.Task.Id)).Select(r => r.Task)] : [row.Task];

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

    internal void SaveTyped()
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
                    {
                        var before = open;
                        ChangeProject(p => p with { Name = EditName });
                        if (open.Id == before.Id && open.Name != before.Name)
                            Files.FolderOffers.OfferRename(services, before, open.Name, () => FolderMadeFor(before.Id));
                    }
                    break;
                case TaskRow row when row.Title.Trim().Length > 0:
                    ChangeTask(row, t => t with { Title = row.Title });
                    break;
                case TaskDetail detail when detail == Detail:
                    detail.SaveTyped();
                    break;
                case var description when description == ProjectOverviewPart.DescriptionTyped && open is not null:
                    OverviewPart.SaveDescription();
                    break;
                case SectionRow { Heading: { } heading } section when section.Name.Trim().Length > 0:
                    Ours(() => services.Projects.ChangeHeading(heading.Id, h => h with { Name = section.Name }));
                    break;
            }
        }
        OnPropertyChanged(nameof(HoldsWork));
        if (refreshWaiting && typed.Count == 0)
        {
            refreshWaiting = false;
            Refresh();
        }
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

    // A change from elsewhere (another copy of the page, Home, the Week page, Claude or ChatGPT) waits while something
    // is being typed here, so the typing isn't thrown away; the page reads it again once that's saved.
    private bool refreshWaiting;

    private void ElsewhereChanged(string? projectId)
    {
        if (ours)
            return;
        if (typed.Count > 0)
        {
            refreshWaiting = true;
            return;
        }
        // My tasks and an open task can show tasks from any project.
        if (projectId is null || projectId == open?.Id || IsMyTasks || Detail is not null)
            Refresh();
        else
            ShowList();
    }

    /// <summary>A note under the project's header, such as a file that's gone.</summary>
    internal void Tell(string message) => Message = message;

    /// <summary>Shows an attached file in the app's Files page, picked.</summary>
    internal void ShowFile(string path) => services.Go.ShowFile(path);

    public void Dispose()
    {
        FilesPart.LetPicturesGo();
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

/// <summary>
/// A heading in the open project (or the tasks under no heading), with its name to change; or, when the list is
/// grouped by when, who or priority, one of those groups. It folds up to its name, as in Asana.
/// </summary>
public sealed partial class SectionRow(ListGroup group, ProjectsViewModel page) : ObservableObject
{
    public ListGroup Group { get; } = group;
    public Heading? Heading => Group.Heading;
    public IReadOnlyList<ProjectTask> Tasks => Group.Tasks;
    public bool IsHeading => Heading is not null;
    public bool IsLoose => Group.IsSection && Heading is null;

    /// <summary>A heading or the tasks under none, rather than a group the Group menu made.</summary>
    public bool IsSection => Group.IsSection;

    /// <summary>A group the Group menu made: its name can't be changed.</summary>
    public bool IsMadeGroup => !Group.IsSection;

    public string Title => Group.Title;
    public string Count => Tasks.Count == 0 ? "" : $"{Tasks.Count(t => t.Done)}/{Tasks.Count}";

    /// <summary>How many tasks it has, by its name on the board, as Asana's.</summary>
    public string Total => Tasks.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenOut))]
    public partial bool IsFolded { get; set; }

    public bool IsOpenOut => !IsFolded;

    [RelayCommand]
    private void Fold() => page.Fold(this);

    [ObservableProperty] public partial string Name { get; set; } = group.Heading?.Name ?? group.Title;

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
    internal bool loading;

    public TaskRow(ProjectTask task, SectionRow? section, string subtasks, IReadOnlyList<Choice<string?>> people, ProjectsViewModel page)
    {
        this.page = page;
        Section = section;
        Subtasks = subtasks;
        People = people;
        Task = task;
        Reset();
    }

    /// <summary>Picked with Ctrl or Shift, to drag along with others.</summary>
    [ObservableProperty] public partial bool IsPicked { get; set; }

    /// <summary>Its subtasks shown under it in the list.</summary>
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    /// <summary>How many comments it has, by a speech bubble in the list, as Asana's.</summary>
    public int CommentCount { get; init; }
    public bool HasComments => CommentCount > 0;
    public string CommentText => CommentCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>How many files are attached, by a paperclip in the list, as Asana's.</summary>
    public string AttachmentCount => Task.Attachments.Count == 0 ? "" : Task.Attachments.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public bool HasAttachments => Task.Attachments.Count > 0;

    /// <summary>Its cover picture, on top of its card on the board.</summary>
    public AttachmentRow? Cover => cover ??= Attachments.Cover(Task) is { } picture ? page.FilesPart.CoverRow(Task, picture) : null;
    private AttachmentRow? cover;
    public bool HasCover => Attachments.Cover(Task) is not null;

    /// <summary>A subtask shown under its task in the list, set in.</summary>
    public bool IsSubtask => Task.ParentId is not null;

    /// <summary>A milestone, shown with a diamond, as Asana's.</summary>
    public bool IsMilestone => Task.Milestone;

    /// <summary>The open tasks it's still waiting on, by name; empty when it isn't waiting.</summary>
    public string WaitingTip { get; init; } = "";
    public bool IsWaiting => WaitingTip.Length > 0;

    /// <summary>Its values for the project's custom fields: cells in the list; the choices show as pills on its card.</summary>
    public IReadOnlyList<FieldCell> FieldCells { get; init; } = [];
    public IReadOnlyList<FieldCell> Pills => [.. FieldCells.Where(c => c.IsPill)];
    public bool HasPills => FieldCells.Any(c => c.IsPill);
    public string WaitingText => "Waiting on " + WaitingTip;

    [RelayCommand]
    private void Expand() => page.Expand(this);

    public string PriorityText => Task.Priority == TaskPriority.Normal ? "" : ProjectRules.Label(Task.Priority);
    public bool HasPriority => Task.Priority != TaskPriority.Normal;

    /// <summary>Who it's for, as initials for the face in the Assignee column.</summary>
    public string WhoInitials => AssignedTo?.Value is null ? "" : AssignedTo.Label;

    internal void SetPriority(TaskPriority priority)
    {
        page.ChangeTask(this, t => t with { Priority = priority });
        Reset();
    }

    /// <summary>What dragging it takes: the picked tasks when it's one of them, otherwise just it.</summary>
    public IReadOnlyList<ProjectTask> Moving => page.Moving(this);

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

    /// <summary>Tags or a repeat to show under the name in the list, where priority has a column of its own.</summary>
    public bool HasListMarks => Task.Tags.Count > 0 || Task.Repeat != TaskRepeat.None;
    public bool Repeats => Task.Repeat != TaskRepeat.None;
    public string RepeatLabel => ProjectRules.Label(Task);

    /// <summary>The line under a card's title on the board: who it's for, when it's due, its subtasks.</summary>
    public string CardLine => string.Join(" · ", new[]
    {
        AssignedTo?.Value is null ? null : AssignedTo.Label,
        Due is { } due ? "Due " + due.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : null,
        Subtasks.Length > 0 ? Subtasks + " subtasks" : null,
    }.Where(t => t is not null));

    public bool HasCardLine => CardLine.Length > 0;

    /// <summary>The foot of a card on the board: a face and a due date on the left, counts on the right.</summary>
    public bool HasCardFoot => HasWho || HasDue || HasSubtasks || HasComments || HasAttachments;

    /// <summary>Who it's for, as the list shows it: a name you click to change, or "No one".</summary>
    public string WhoText => AssignedTo?.Value is null ? "No one" : AssignedTo.Label;
    public bool HasWho => AssignedTo?.Value is not null;

    /// <summary>When it's due, as the list shows it ("Tomorrow", "12 Nov"), or "Due date" when it has none.</summary>
    public string DueText => Due is { } due
        ? ProjectRules.DueLabel(DateOnly.FromDateTime(due), DateOnly.FromDateTime(DateTime.Today))
          + (Task.DueTime is { } time ? ", " + time.ToString("t", System.Globalization.CultureInfo.CurrentCulture) : "")
        : "Due date";
    public bool HasDue => Due is not null;
    public bool IsLate => ProjectRules.IsOverdue(Task with { Done = Done, Due = Due is { } due ? DateOnly.FromDateTime(due) : null }, DateTime.Now);

    [ObservableProperty] public partial Choice<string?>? AssignedTo { get; set; }
    [ObservableProperty] public partial DateTime? Due { get; set; }
    [ObservableProperty] public partial bool IsOpen { get; set; }

    /// <summary>Shows the task as stored, e.g. after a change that wasn't allowed.</summary>
    public void Reset()
    {
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(IsHigh));
        OnPropertyChanged(nameof(IsLow));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasMarks));
        OnPropertyChanged(nameof(HasListMarks));
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(HasPriority));
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
        if (loading)
            return;
        page.ChangeTask(this, t => t with { Done = value });
        if (value && IsWaiting)
            page.Tell($"Done, though it was still waiting on {WaitingTip}.");
    }

    partial void OnAssignedToChanged(Choice<string?>? value)
    {
        OnPropertyChanged(nameof(WhoText));
        OnPropertyChanged(nameof(HasWho));
        OnPropertyChanged(nameof(CardLine));
        OnPropertyChanged(nameof(HasCardLine));
        OnPropertyChanged(nameof(WhoInitials));
        OnPropertyChanged(nameof(HasCardFoot));
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
        OnPropertyChanged(nameof(HasCardFoot));
        if (!loading)
            page.ChangeTask(this, t => t with { Due = value is { } d ? DateOnly.FromDateTime(d) : null });
    }

    [RelayCommand]
    private void Delete() => page.DeleteTask(this);
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
}

/// <summary>
/// The box under a heading's tasks for adding one. On the board it opens when asked, from "Add task" under a
/// column or the + at its top, as Asana's.
/// </summary>
public sealed partial class AddTaskRow(SectionRow section, ProjectsViewModel page) : ObservableObject
{
    public SectionRow Section { get; } = section;

    [ObservableProperty] public partial string Title { get; set; } = "";

    /// <summary>The board column's box is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypingTop), nameof(IsTypingBottom), nameof(ShowsAddButton))]
    public partial bool IsTyping { get; set; }

    /// <summary>The new card goes at the top of the column, from its +.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypingTop), nameof(IsTypingBottom))]
    public partial bool AtTop { get; set; }

    public bool IsTypingTop => IsTyping && AtTop;
    public bool IsTypingBottom => IsTyping && !AtTop;
    public bool ShowsAddButton => !IsTyping;

    [RelayCommand]
    private void Add()
    {
        var title = Title;
        Title = "";
        page.AddTask(Section, title);
    }

    /// <summary>Adds the card typed on the board, keeping the box open for the next.</summary>
    public void AddCard()
    {
        var title = Title;
        Title = "";
        if (title.Trim().Length == 0)
            IsTyping = false;
        else
            page.AddTask(Section, title, AtTop, keepTyping: true);
    }
}

/// <summary>The views of an open project: the list, the board, its files, the overview, the timeline, the calendar and its messages.</summary>
public enum ProjectTab { List, Board, Files, Overview, Timeline, Calendar, Messages }
