using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core;
using Webignite.Core.Clients;
using Webignite.Core.Projects;
using Webignite.Core.Time;

namespace Webignite.Desktop.ViewModels;

// A client's projects: one list, kept in the Planner, used for tasks, time and mail. Each shows when it's due, the
// time logged on it and its own rate; ticking it finished marks it Done in the Planner. Saved straight away.
public partial class ClientDetailViewModel
{
    public ObservableCollection<ClientProjectRow> ProjectRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProjectCommand))]
    public partial string NewProjectName { get; set; } = "";

    /// <summary>Who a new project is for, when the client is an agency passing on its own client's work.</summary>
    [ObservableProperty] public partial string NewProjectFor { get; set; } = "";

    /// <summary>"12 h 30 min in all, 3 h this month" for the client.</summary>
    [ObservableProperty] public partial string TimeSummary { get; set; } = "";

    public bool HasProjects => ProjectRows.Count > 0;

    private void LoadProjects()
    {
        ProjectRows.Clear();
        if (isNew)
        {
            TimeSummary = "No time logged yet.";
            OnPropertyChanged(nameof(HasProjects));
            return;
        }
        // All time for the client: a small business logs a few thousand entries at most, so reading them is cheap.
        var entries = services.Store.ListTime(DateOnly.MinValue, DateOnly.MaxValue).Where(e => e.ClientId == client.Id).ToList();
        var month = Dates.MonthStart(Today);
        var all = entries.Sum(e => e.Minutes);
        var thisMonth = entries.Where(e => e.Date >= month).Sum(e => e.Minutes);
        TimeSummary = all == 0 ? "No time logged yet." : $"{TimeRules.Format(all)} in all, {TimeRules.Format(thisMonth)} this month.";
        foreach (var project in ClientPlanner.For(services.Projects.Projects(), client.Id))
            ProjectRows.Add(new ClientProjectRow(project, TimeRules.Format(entries.Where(e => e.ProjectId == project.Id).Sum(e => e.Minutes)), Today, this));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(ProjectsSummary));
    }

    private bool CanAddProject() => IsSaved && !string.IsNullOrWhiteSpace(NewProjectName);

    [RelayCommand(CanExecute = nameof(CanAddProject))]
    private void AddProject()
    {
        var (saved, problems) = services.Projects.Add(new Project
        {
            Id = ProjectRules.NewId(), Name = NewProjectName.Trim(), ClientId = client.Id, Shared = true,
            For = string.IsNullOrWhiteSpace(NewProjectFor) ? null : NewProjectFor.Trim(),
        });
        if (saved is null)
        {
            services.ShowNote?.Invoke("Couldn't add the project", string.Join(" ", problems), null);
            return;
        }
        NewProjectName = NewProjectFor = "";
        LoadProjects();
    }

    /// <summary>Sets or clears a project's own hourly rate. Returns false when the text isn't an amount.</summary>
    internal bool SetProjectRate(Project project, string text)
    {
        if (!TimeRules.TryParseRate(text, out var rate))
            return false;
        if (rate != project.HourlyRate)
            ChangeProject(project.Id, p => p with { HourlyRate = rate });
        return true;
    }

    internal void SetProjectFinished(Project project, bool finished) =>
        ChangeProject(project.Id, p => p with { Status = finished ? ProjectStatus.Done : p.Status == ProjectStatus.Done ? ProjectStatus.Active : p.Status });

    internal void OpenProject(Project project) => services.Go.Project(project.Id);

    private void ChangeProject(string id, Func<Project, Project> change)
    {
        var (_, problems) = services.Projects.Change(id, change);
        if (problems.Count > 0)
            services.ShowNote?.Invoke("Couldn't change the project", string.Join(" ", problems), null);
        LoadProjects();
    }
}

/// <summary>A project on the client's page. Its name opens it in the Planner; ticking it finished marks it Done there.</summary>
public sealed partial class ClientProjectRow : ObservableObject
{
    private readonly Project project;
    private readonly ClientDetailViewModel owner;
    private bool finished;

    public ClientProjectRow(Project project, string time, DateOnly today, ClientDetailViewModel owner)
    {
        this.project = project;
        this.owner = owner;
        Time = time;
        finished = project.Status == ProjectStatus.Done;
        Line = ClientPlanner.Line(project, today, d => d.ToString(d.Year == today.Year ? "d MMM" : "d MMM yyyy"));
        IsLate = ClientPlanner.IsLate(project, today);
    }

    public string Name => project.Name;
    public string For => string.IsNullOrWhiteSpace(project.For) ? "" : "For " + project.For.Trim();
    public bool HasFor => For.Length > 0;
    public string Time { get; }
    public string Line { get; }
    public bool IsLate { get; }

    /// <summary>The project's own hourly rate as typed; blank uses the client's rate. Saved when the box loses focus.</summary>
    public string Rate
    {
        get => project.HourlyRate is { } rate ? rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
        set => RateProblem = !owner.SetProjectRate(project, value);
    }

    [ObservableProperty] public partial bool RateProblem { get; set; }

    public bool Finished
    {
        get => finished;
        set
        {
            if (SetProperty(ref finished, value))
                owner.SetProjectFinished(project, value);
        }
    }

    [RelayCommand]
    private void Open() => owner.OpenProject(project);
}
