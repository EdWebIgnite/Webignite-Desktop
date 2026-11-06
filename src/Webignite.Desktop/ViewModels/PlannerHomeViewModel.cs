using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

public enum HomeTab
{
    Upcoming,
    Overdue,
    Completed,
}

/// <summary>
/// Planner's Home, laid out as Asana's: the date and a greeting, how many tasks I finished this week, my tasks in
/// three tabs, and the projects worked on lately with a way to start a new one.
/// </summary>
public sealed partial class PlannerHomeViewModel : ViewModelBase, IRefreshable, IDisposable
{
    private const int RecentCount = 8;
    private readonly AppServices services;

    public PlannerHomeViewModel(AppServices services)
    {
        this.services = services;
        services.Projects.Changed += ProjectsChanged;
        Refresh();
    }

    public string DateText => DateTime.Today.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);

    public string Greeting => $"{PlannerHome.Greeting(DateTime.Now.Hour)}, {services.Me().Name}";

    public string MyName => services.Me().Name;

    [ObservableProperty] public partial string CompletedText { get; private set; } = "";

    // My tasks

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpcoming), nameof(IsOverdue), nameof(IsCompleted))]
    public partial HomeTab Tab { get; private set; }

    public bool IsUpcoming => Tab == HomeTab.Upcoming;
    public bool IsOverdue => Tab == HomeTab.Overdue;
    public bool IsCompleted => Tab == HomeTab.Completed;

    [ObservableProperty] public partial string OverdueLabel { get; private set; } = "Overdue";

    public ObservableCollection<HomeTaskRow> Tasks { get; } = [];

    [ObservableProperty] public partial string EmptyText { get; private set; } = "";

    public bool HasNoTasks => Tasks.Count == 0;

    [RelayCommand]
    private void ShowTab(HomeTab tab)
    {
        Tab = tab;
        ShowTasks();
    }

    // Projects

    /// <summary>The Projects card: a tile to create one, then the projects worked on lately, the starred ones, or all in my order.</summary>
    public ObservableCollection<object> Projects { get; } = [];

    public HomeProjects ProjectsShown => services.Settings.Home.Projects;

    public string ProjectsShownText => PlannerHome.Label(ProjectsShown);

    /// <summary>Tiles can be dragged into order when the card shows the starred projects or all in my order.</summary>
    public bool CanArrange => ProjectsShown != HomeProjects.Recent;

    public static IReadOnlyList<HomeProjects> ProjectsShownChoices { get; } = [HomeProjects.Recent, HomeProjects.Starred, HomeProjects.Ordered];

    [RelayCommand]
    private void ShowProjects(HomeProjects which)
    {
        services.Settings.Home = services.Settings.Home with { Projects = which };
        OnPropertyChanged(nameof(ProjectsShown));
        OnPropertyChanged(nameof(ProjectsShownText));
        OnPropertyChanged(nameof(CanArrange));
        Refresh();
    }

    /// <summary>Whether a tile dragged onto another would land before or after it; it takes the other's star.</summary>
    public bool CanMoveTile(object dragged, object target, bool after) =>
        CanArrange && dragged is HomeProjectTile moving && target is HomeProjectTile on
        && ProjectLooks.PlaceAt(Arranged(), moving.Id, on.Id, after) is not null;

    /// <summary>Drops a dragged tile before or after another; the order is the sidebar's too.</summary>
    public void MoveTile(object dragged, object target, bool after)
    {
        if (!CanMoveTile(dragged, target, after) || dragged is not HomeProjectTile moving || target is not HomeProjectTile on
            || ProjectLooks.PlaceAt(Arranged(), moving.Id, on.Id, after) is not { } place)
            return;
        services.Projects.Change(moving.Id, p => p with { Order = place.Order, Favourite = place.Favourite });
    }

    // The projects in the order the card puts them, all of them and not only the first few shown.
    private IReadOnlyList<Project> Arranged() => PlannerHome.Shown(services.Projects.Projects(), ProjectsShown, int.MaxValue);

    [RelayCommand]
    private void BrowseProjects() => services.Go.To(Page.Projects);

    [RelayCommand]
    private void CreateProject() => services.Go.NewProject();

    [RelayCommand]
    private void OpenMyTasks() => services.Go.MyTasks();

    private void ProjectsChanged(string? id) => Refresh();

    public void Refresh()
    {
        var me = services.Me().Id;
        var all = services.Projects.Projects().SelectMany(p => services.Projects.AllTasks(p.Id)).ToList();
        var done = PlannerHome.Completed(all, me, PlannerHome.WeekStart(DateTimeOffset.Now)).Count;
        CompletedText = done == 1 ? "1 task completed" : $"{done} tasks completed";
        OverdueLabel = PlannerHome.Overdue(services.Projects.Mine(me), Today) is { Count: > 0 } late ? $"Overdue ({late.Count})" : "Overdue";
        ShowTasks();
        Projects.Clear();
        Projects.Add(new HomeCreateTile(CreateProjectCommand));
        foreach (var project in PlannerHome.Shown(services.Projects.Projects(), ProjectsShown, RecentCount))
            Projects.Add(new HomeProjectTile(project, services));
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(DateText));
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private void ShowTasks()
    {
        var me = services.Me().Id;
        var names = services.Projects.Projects().ToDictionary(p => p.Id, p => p.Name);
        IReadOnlyList<ProjectTask> shown = Tab switch
        {
            HomeTab.Overdue => PlannerHome.Overdue(services.Projects.Mine(me), Today),
            HomeTab.Completed => PlannerHome.Completed(
                services.Projects.Projects().SelectMany(p => services.Projects.AllTasks(p.Id)), me, DateTimeOffset.Now.AddDays(-14)),
            _ => PlannerHome.Upcoming(services.Projects.Mine(me), Today),
        };
        Tasks.Clear();
        foreach (var task in shown)
            Tasks.Add(new HomeTaskRow(task, names.GetValueOrDefault(task.ProjectId) ?? "", Today, services));
        EmptyText = Tab switch
        {
            HomeTab.Overdue => "Nothing overdue. Well done.",
            HomeTab.Completed => "Tasks you finish in the next while show here.",
            _ => "Nothing coming up. Tasks given to you show here, soonest first.",
        };
        OnPropertyChanged(nameof(HasNoTasks));
    }

    public void Dispose() => services.Projects.Changed -= ProjectsChanged;
}

/// <summary>A task on Home: tick it off, see its project and when it's due, click to open it.</summary>
public sealed partial class HomeTaskRow : ObservableObject
{
    private readonly AppServices services;
    private readonly bool loading;

    public HomeTaskRow(ProjectTask task, string project, DateOnly today, AppServices services)
    {
        this.services = services;
        Task = task;
        Project = project;
        DueText = task.Due is { } due ? ProjectRules.DueLabel(due, today) : "";
        IsLate = !task.Done && task.Due < today;
        loading = true;
        Done = task.Done;
        loading = false;
    }

    public ProjectTask Task { get; }
    public string Title => Task.Title;
    public string Project { get; }
    public string DueText { get; }
    public bool IsLate { get; }

    [ObservableProperty] public partial bool Done { get; set; }

    partial void OnDoneChanged(bool value)
    {
        if (!loading)
            services.Projects.ChangeTask(Task.Id, t => t with { Done = value });
    }

    [RelayCommand]
    private void Open() => services.Go.ProjectTask(Task.Id);
}

/// <summary>A project on Home: its name and a tile to open it.</summary>
public sealed partial class HomeProjectTile(Project project, AppServices services)
{
    public string Id => project.Id;
    public string Name => project.Name;
    public string Colour => ProjectLooks.ColourOf(project);
    public string Icon => ProjectLooks.IconOf(project);
    public string Initials => string.Concat(project.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    [RelayCommand]
    private void Open() => services.Go.Project(project.Id);
}

/// <summary>The first tile on the Projects card, to make a new project.</summary>
public sealed class HomeCreateTile(System.Windows.Input.ICommand create)
{
    public System.Windows.Input.ICommand Create { get; } = create;
}
