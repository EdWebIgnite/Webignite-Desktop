using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// Home's widgets, as Asana's Customise: My tasks and Projects to start with, and a private notepad, people, status
// updates and milestones to add. Each is half or full width, dragged into order by its heading, kept in Home's settings.
public sealed partial class PlannerHomeViewModel
{
    private const double Gap = 24;
    private const double SideBySide = 900;
    private readonly DispatcherTimer notepadTyping = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool notepadTyped;

    public ObservableCollection<HomeWidgetRow> Widgets { get; } = [];
    public ObservableCollection<HomeWidgetChoice> WidgetChoices { get; } = [];

    public bool NoWidgets => Widgets.Count == 0;

    /// <summary>How wide the widgets' area is, set by the view; half-width widgets share it two by two.</summary>
    public double LayoutWidth
    {
        get;
        set
        {
            if (Math.Abs(field - value) < 0.5)
                return;
            field = value;
            foreach (var widget in Widgets)
                widget.Fit(value);
        }
    }

    internal static double CardWidth(double width, bool full) =>
        width <= 0 ? double.NaN : full || width < SideBySide ? width : Math.Floor((width - Gap) / 2);

    private HomeSettings HomeSettings
    {
        get => services.Settings.Home;
        set
        {
            services.Settings.Home = value;
            ShowWidgets();
        }
    }

    private void ShowWidgets()
    {
        var widgets = PlannerHome.Widgets(HomeSettings);
        Widgets.Clear();
        foreach (var widget in widgets)
        {
            HomeWidgetRow row = widget.Kind switch
            {
                HomeWidgetKind.MyTasks => new HomeTasksWidget(widget, this),
                HomeWidgetKind.Projects => new HomeProjectsWidget(widget, this),
                HomeWidgetKind.Notepad => new HomeNotepadWidget(widget, this),
                HomeWidgetKind.People => new HomePeopleWidget(widget, this),
                HomeWidgetKind.StatusUpdates => new HomeStatusWidget(widget, this),
                _ => new HomeMilestonesWidget(widget, this),
            };
            row.Fit(LayoutWidth);
            Widgets.Add(row);
        }
        WidgetChoices.Clear();
        foreach (var kind in Enum.GetValues<HomeWidgetKind>())
            WidgetChoices.Add(new HomeWidgetChoice(kind, widgets.Any(w => w.Kind == kind), this));
        OnPropertyChanged(nameof(NoWidgets));
    }

    internal void ToggleWidget(HomeWidgetKind kind, bool on) =>
        HomeSettings = on ? PlannerHome.Add(HomeSettings, kind) : PlannerHome.Remove(HomeSettings, kind);

    internal void ResizeWidget(HomeWidgetKind kind) => HomeSettings = PlannerHome.Resize(HomeSettings, kind);

    public bool CanMoveWidget(object dragged, object target) => dragged is HomeWidgetRow moving && target is HomeWidgetRow on && moving != on;

    public void MoveWidget(object dragged, object target, bool after)
    {
        if (dragged is HomeWidgetRow moving && target is HomeWidgetRow on && moving != on)
            HomeSettings = PlannerHome.Move(HomeSettings, moving.Kind, on.Kind, after);
    }

    // The private notepad

    [ObservableProperty] public partial string Notepad { get; set; } = "";

    partial void OnNotepadChanged(string value)
    {
        if (loadingNotepad)
            return;
        notepadTyped = true;
        notepadTyping.Stop();
        notepadTyping.Start();
    }

    private bool loadingNotepad;

    private void LoadNotepad()
    {
        loadingNotepad = true;
        Notepad = HomeSettings.Notepad;
        loadingNotepad = false;
    }

    /// <summary>Saves the notepad's words; a moment after typing stops, and when the page closes.</summary>
    public void KeepNotepad()
    {
        notepadTyping.Stop();
        if (!notepadTyped)
            return;
        notepadTyped = false;
        services.Settings.Home = services.Settings.Home with { Notepad = Notepad };
    }

    // People, status updates and milestones

    public ObservableCollection<HomePersonLine> People { get; } = [];
    public ObservableCollection<HomeStatusLine> StatusUpdates { get; } = [];
    public ObservableCollection<HomeMilestoneLine> Milestones { get; } = [];

    public bool NoStatusUpdates => StatusUpdates.Count == 0;
    public bool NoMilestones => Milestones.Count == 0;

    private void LoadWidgetData(IReadOnlyList<ProjectTask> all)
    {
        var team = services.Team();
        People.Clear();
        foreach (var person in new[] { team.Me!, team.Partner! })
        {
            var (open, late) = PlannerHome.Load(all, person.Id, Today);
            People.Add(new HomePersonLine(person.Name, person.Id == team.Me!.Id, open, late, services));
        }
        var projects = services.Projects.Projects();
        string NameOf(string id) => team.NameOf(id);
        StatusUpdates.Clear();
        foreach (var (project, update) in PlannerHome.LatestUpdates(projects, 6))
            StatusUpdates.Add(new HomeStatusLine(project, update, NameOf(update.By), services));
        var names = projects.ToDictionary(p => p.Id, p => p.Name);
        Milestones.Clear();
        foreach (var milestone in PlannerHome.Milestones(projects, all, 8))
            Milestones.Add(new HomeMilestoneLine(milestone, names.GetValueOrDefault(milestone.ProjectId) ?? "", Today, services));
        OnPropertyChanged(nameof(NoStatusUpdates));
        OnPropertyChanged(nameof(NoMilestones));
    }
}

/// <summary>A widget on Home: what it shows, how wide it is, and its menu (make it full or half width, take it off).</summary>
public abstract partial class HomeWidgetRow(HomeWidget widget, PlannerHomeViewModel page) : ObservableObject
{
    public HomeWidgetKind Kind => widget.Kind;
    public bool Full => widget.Full;
    public PlannerHomeViewModel Page { get; } = page;
    public string Title => PlannerHome.Label(widget.Kind);
    public string SizeText => widget.Full ? "Make it half width" : "Make it full width";

    [ObservableProperty] public partial double Width { get; private set; } = double.NaN;

    internal void Fit(double width) => Width = PlannerHomeViewModel.CardWidth(width, widget.Full);

    [RelayCommand]
    private void Resize() => Page.ResizeWidget(widget.Kind);

    [RelayCommand]
    private void Remove() => Page.ToggleWidget(widget.Kind, false);
}

public sealed class HomeTasksWidget(HomeWidget widget, PlannerHomeViewModel page) : HomeWidgetRow(widget, page);
public sealed class HomeProjectsWidget(HomeWidget widget, PlannerHomeViewModel page) : HomeWidgetRow(widget, page);
public sealed class HomeNotepadWidget(HomeWidget widget, PlannerHomeViewModel page) : HomeWidgetRow(widget, page);
public sealed class HomePeopleWidget(HomeWidget widget, PlannerHomeViewModel page) : HomeWidgetRow(widget, page);
public sealed class HomeStatusWidget(HomeWidget widget, PlannerHomeViewModel page) : HomeWidgetRow(widget, page);
public sealed class HomeMilestonesWidget(HomeWidget widget, PlannerHomeViewModel page) : HomeWidgetRow(widget, page);

/// <summary>A widget in the Customise panel: on Home or not, ticked to add it or take it off.</summary>
public sealed partial class HomeWidgetChoice : ObservableObject
{
    private readonly PlannerHomeViewModel page;
    private readonly bool loading;

    public HomeWidgetChoice(HomeWidgetKind kind, bool on, PlannerHomeViewModel page)
    {
        this.page = page;
        Kind = kind;
        loading = true;
        IsOn = on;
        loading = false;
    }

    public HomeWidgetKind Kind { get; }
    public string Title => PlannerHome.Label(Kind);
    public string About => PlannerHome.About(Kind);

    [ObservableProperty] public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        if (!loading)
            page.ToggleWidget(Kind, value);
    }
}

/// <summary>Someone on the People widget, with what's on their plate.</summary>
public sealed partial class HomePersonLine(string name, bool isMe, int open, int late, AppServices services)
{
    public string Name { get; } = name;
    public bool IsMe { get; } = isMe;
    public string Role => IsMe ? "You" : "Your partner";
    public string Load => (open, late) switch
    {
        (0, _) => "Nothing on their plate",
        (_, 0) => open == 1 ? "1 open task" : $"{open} open tasks",
        _ => (open == 1 ? "1 open task" : $"{open} open tasks") + $", {late} late",
    };
    public bool HasLate { get; } = late > 0;

    [RelayCommand]
    private void Open()
    {
        if (IsMe)
            services.Go.MyTasks();
        else
            services.Go.To(Page.Projects);
    }
}

/// <summary>A project's newest status update, on the Status updates widget.</summary>
public sealed partial class HomeStatusLine(Project project, StatusUpdate update, string who, AppServices services)
{
    public string Project { get; } = project.Name;
    public ProjectHealth Health { get; } = update.Health;
    public string HealthText { get; } = ProjectLooks.Label(update.Health);
    public string Text { get; } = update.Text;
    public bool HasText { get; } = update.Text.Length > 0;
    public string Byline { get; } = $"{who} · {TaskTalk.When(update.At, DateTimeOffset.Now, TimeZoneInfo.Local, CultureInfo.CurrentCulture)}";

    [RelayCommand]
    private void Open() => services.Go.Project(project.Id);
}

/// <summary>A milestone coming up, on the Milestones widget.</summary>
public sealed partial class HomeMilestoneLine(ProjectTask task, string project, DateOnly today, AppServices services)
{
    public string Title { get; } = task.Title;
    public string Project { get; } = project;
    public string DueText { get; } = task.Due is { } due ? ProjectRules.DueLabel(due, today) : "No date";
    public bool IsLate { get; } = task.Due < today;

    [RelayCommand]
    private void Open() => services.Go.ProjectTask(task.Id);
}
