using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The Overview tab, as Asana's: what the project is about, who's on it, its key links and milestones on the left;
// how it's going (status updates), its details and how far it has come on the right.
public sealed partial class ProjectOverviewPart(AppServices services, ProjectsViewModel page) : ViewModelBase
{
    internal static readonly object DescriptionTyped = new();

    [ObservableProperty] public partial Webignite.Core.Writing.RichDocument ProjectDescription { get; set; } = Webignite.Core.Writing.RichDocument.Empty();

    /// <summary>Goes up as the description is typed in, so it's saved a moment later; 0 when it was loaded.</summary>
    [ObservableProperty] public partial int ProjectDescriptionRevision { get; set; }

    partial void OnProjectDescriptionRevisionChanged(int value)
    {
        if (value > 0 && !page.loading)
            page.Typed(DescriptionTyped);
    }

    internal void SaveDescription()
    {
        var description = ProjectDescription.Clone();
        page.ChangeProject(p => ProjectOverview.WithDescription(p, description));
    }

    public ObservableCollection<RoleRow> Roles { get; } = [];
    public ObservableCollection<LinkRow> Links { get; } = [];
    public ObservableCollection<TaskRow> Milestones { get; } = [];
    public ObservableCollection<UpdateRow> Updates { get; } = [];

    public bool NoLinks => Links.Count == 0;
    public bool NoMilestones => Milestones.Count == 0;
    public bool NoUpdates => Updates.Count == 0;

    [ObservableProperty] public partial string NewLinkTitle { get; set; } = "";
    [ObservableProperty] public partial string NewLinkUrl { get; set; } = "";
    [ObservableProperty] public partial bool IsAddingLink { get; set; }
    [ObservableProperty] public partial string NewMilestone { get; set; } = "";

    /// <summary>The status picked for the next update; None until one of the three is clicked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWritingUpdate), nameof(UpdateOnTrack), nameof(UpdateAtRisk), nameof(UpdateOffTrack))]
    public partial ProjectHealth UpdateHealth { get; set; }

    public bool IsWritingUpdate => UpdateHealth != ProjectHealth.None;
    public bool UpdateOnTrack => UpdateHealth == ProjectHealth.OnTrack;
    public bool UpdateAtRisk => UpdateHealth == ProjectHealth.AtRisk;
    public bool UpdateOffTrack => UpdateHealth == ProjectHealth.OffTrack;

    [ObservableProperty] public partial string UpdateText { get; set; } = "";

    [ObservableProperty] public partial string ProgressText { get; private set; } = "";
    [ObservableProperty] public partial double ProgressShare { get; private set; }
    [ObservableProperty] public partial string LateText { get; private set; } = "";
    [ObservableProperty] public partial string MadeText { get; private set; } = "";

    public bool HasLate => LateText.Length > 0;

    partial void OnLateTextChanged(string value) => OnPropertyChanged(nameof(HasLate));

    private string NameOf(string? id)
    {
        return services.Team().NameOf(id);
    }

    internal void LoadOverview()
    {
        if (page.open is null || !page.IsOverview)
            return;
        var wasLoading = page.loading;
        page.loading = true;
        ProjectDescription = ProjectOverview.Description(page.open);
        ProjectDescriptionRevision = 0;
        page.loading = wasLoading;

        var team = services.Team();
        Roles.Clear();
        Roles.Add(new RoleRow(team.Me!.Name, "Project owner"));
        if (page.open.Shared)
            Roles.Add(new RoleRow(team.Partner!.Name, "Member"));

        Links.Clear();
        foreach (var link in page.open.Links)
            Links.Add(new LinkRow(link, page));

        var tasks = services.Projects.AllTasks(page.open.Id);
        Milestones.Clear();
        foreach (var milestone in ProjectOverview.Milestones(tasks))
            Milestones.Add(new TaskRow(milestone, null, "", page.People, page));

        var now = DateTimeOffset.Now;
        Updates.Clear();
        foreach (var update in page.open.Updates)
            Updates.Add(new UpdateRow(update, NameOf(update.By), TaskTalk.When(update.At, now, TimeZoneInfo.Local, CultureInfo.CurrentCulture)));

        var progress = ProjectOverview.Progress(tasks, DateOnly.FromDateTime(DateTime.Today));
        ProgressShare = progress.Share * 100;
        ProgressText = progress.Total == 0 ? "No tasks yet" : $"{progress.Done} of {progress.Total} tasks done";
        LateText = progress.Late switch { 0 => "", 1 => "1 task is late", _ => $"{progress.Late} tasks are late" };
        MadeText = $"Made by {NameOf(page.open.Created.By)} on {page.open.Created.At.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)}";

        OnPropertyChanged(nameof(NoLinks));
        OnPropertyChanged(nameof(NoMilestones));
        OnPropertyChanged(nameof(NoUpdates));
    }

    [RelayCommand]
    private void StartUpdate(ProjectHealth health) => UpdateHealth = health;

    [RelayCommand]
    private void CancelUpdate()
    {
        UpdateHealth = ProjectHealth.None;
        UpdateText = "";
    }

    /// <summary>Posts the status update written; the header's status follows it.</summary>
    [RelayCommand]
    private void PostUpdate()
    {
        if (page.open is null)
            return;
        var (saved, problems) = page.Ours(() => services.Projects.PostStatus(page.open.Id, UpdateHealth, UpdateText));
        if (saved is null)
        {
            page.Message = string.Join(" ", problems);
            return;
        }
        page.open = saved;
        page.Message = "";
        page.EditHealth = saved.Health;
        UpdateHealth = ProjectHealth.None;
        UpdateText = "";
        LoadOverview();
    }

    [RelayCommand]
    private void StartLink() => IsAddingLink = true;

    [RelayCommand]
    private void AddLink()
    {
        if (page.open is null)
            return;
        var (saved, problems) = page.Ours(() => services.Projects.AddLink(page.open.Id, NewLinkTitle, NewLinkUrl));
        if (saved is null)
        {
            page.Message = string.Join(" ", problems);
            return;
        }
        page.open = saved;
        page.Message = "";
        NewLinkTitle = "";
        NewLinkUrl = "";
        IsAddingLink = false;
        LoadOverview();
    }

    [RelayCommand]
    private void CancelLink()
    {
        NewLinkTitle = "";
        NewLinkUrl = "";
        IsAddingLink = false;
    }

    internal void RemoveLink(LinkRow row)
    {
        if (page.open is null)
            return;
        var (saved, _) = page.Ours(() => services.Projects.RemoveLink(page.open.Id, row.Link.Id));
        if (saved is not null)
            page.open = saved;
        LoadOverview();
    }

    /// <summary>Adds a milestone: a task marked with a diamond, under no heading, as Asana's.</summary>
    [RelayCommand]
    private void AddMilestone()
    {
        if (page.open is null || NewMilestone.Trim().Length == 0)
            return;
        var (saved, problems) = page.Ours(() => services.Projects.AddTask(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = page.open.Id, Title = NewMilestone, Milestone = true,
        }));
        page.Message = saved is null ? string.Join(" ", problems) : "";
        if (saved is null)
            return;
        NewMilestone = "";
        page.ShowTasks();
    }

    /// <summary>Marks a task as a milestone, or a milestone as a task again, from its right-click menu.</summary>
    public void SetMilestone(TaskRow row, bool milestone)
    {
        page.ChangeTask(row, t => t with { Milestone = milestone });
        page.ShowTasks();
    }
}

/// <summary>Someone on the project, as Asana's Project roles.</summary>
public sealed record RoleRow(string Name, string Role);

/// <summary>A key resource on the Overview: opens in the browser, or comes off.</summary>
public sealed partial class LinkRow(Webignite.Core.Projects.ProjectLink link, ProjectsViewModel page) : ObservableObject
{
    public Webignite.Core.Projects.ProjectLink Link { get; } = link;
    public string Title => Link.Title;
    public string Url => Link.Url;

    [RelayCommand]
    private void Open()
    {
        if (Uri.TryCreate(Link.Url, UriKind.Absolute, out var url))
            AppServices.OpenInBrowser(url).Forget("Opening a project link", tell: true);
    }

    [RelayCommand]
    private void Remove() => page.OverviewPart.RemoveLink(this);
}

/// <summary>A status update in the Overview's list: how it was going, what was said, who and when.</summary>
public sealed record UpdateRow(StatusUpdate Update, string Who, string When)
{
    public ProjectHealth Health => Update.Health;
    public string HealthText => ProjectLooks.Label(Update.Health);
    public string Text => Update.Text;
    public bool HasText => Update.Text.Length > 0;
    public string Byline => $"{Who} · {When}";
}
