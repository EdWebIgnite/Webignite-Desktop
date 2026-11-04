using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Time;
using Webignite.Core;

namespace Webignite.Desktop.ViewModels;

// Projects on a client's page, for logging time against, with the time spent on each. Saved straight away.
public partial class ClientDetailViewModel
{
    public ObservableCollection<ClientProjectRow> ProjectRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProjectCommand))]
    public partial string NewProjectName { get; set; } = "";

    /// <summary>"12 h 30 min in all, 3 h this month" for the client.</summary>
    [ObservableProperty] public partial string TimeSummary { get; set; } = "";

    public bool HasProjects => ProjectRows.Count > 0;

    private void LoadProjects()
    {
        // All time for the client: a small business logs a few thousand entries at most, so reading them is cheap.
        var entries = services.Store.ListTime(DateOnly.MinValue, DateOnly.MaxValue).Where(e => e.ClientId == client.Id).ToList();
        var month = Dates.MonthStart(Today);
        var all = entries.Sum(e => e.Minutes);
        var thisMonth = entries.Where(e => e.Date >= month).Sum(e => e.Minutes);
        TimeSummary = all == 0 ? "No time logged yet." : $"{TimeRules.Format(all)} in all, {TimeRules.Format(thisMonth)} this month.";
        ProjectRows.Clear();
        foreach (var project in client.Projects.OrderBy(p => p.Finished))
            ProjectRows.Add(new ClientProjectRow(project, TimeRules.Format(entries.Where(e => e.ProjectId == project.Id).Sum(e => e.Minutes)), this));
        OnPropertyChanged(nameof(HasProjects));
    }

    private void SaveProjects(Func<IReadOnlyList<ClientProject>, IReadOnlyList<ClientProject>> change)
    {
        client = services.Clients.Change(client.Id, c => c with { Projects = change(c.Projects) }) ?? client;
        LoadProjects();
    }

    private bool CanAddProject() => IsSaved && !string.IsNullOrWhiteSpace(NewProjectName);

    [RelayCommand(CanExecute = nameof(CanAddProject))]
    private void AddProject()
    {
        SaveProjects(projects => [.. projects, new ClientProject { Id = ClientProject.NewId(), Name = NewProjectName.Trim() }]);
        NewProjectName = "";
    }

    /// <summary>Sets or clears a project's own hourly rate. Returns false when the text isn't an amount.</summary>
    internal bool SetProjectRate(ClientProject project, string text)
    {
        if (!TimeRules.TryParseRate(text, out var rate))
            return false;
        if (rate != project.HourlyRate)
            SaveProjects(projects => projects.Select(p => p.Id == project.Id ? p with { HourlyRate = rate } : p).ToList());
        return true;
    }

    internal void SetProjectFinished(ClientProject project, bool finished) =>
        SaveProjects(projects => projects.Select(p => p.Id == project.Id ? p with { Finished = finished } : p).ToList());
}

/// <summary>A project on the client's page. Ticking it finished keeps its time but stops offering it on the Time page.</summary>
public sealed partial class ClientProjectRow : ObservableObject
{
    private readonly ClientProject project;
    private readonly ClientDetailViewModel owner;

    public ClientProjectRow(ClientProject project, string time, ClientDetailViewModel owner)
    {
        this.project = project;
        this.owner = owner;
        Time = time;
        finished = project.Finished;
    }

    private bool finished;

    public string Name => project.Name;
    public string Time { get; }

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
}
