using System.ComponentModel;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The sidebar's Projects group: My tasks, each running project by name, and the templates, each opening that
// part of the Projects page. The entry for what the page shows is the one marked.
public partial class MainViewModel
{
    private readonly SectionLink plannerSection = new("planner", "Planner", "Planner");
    private readonly ProjectLink myTasksLink = new(ProjectLinkKind.MyTasks, null, "My tasks", "Check");
    private readonly ProjectLink templatesLink = new(ProjectLinkKind.Templates, null, "Templates", "Tiles");
    private readonly Dictionary<string, ProjectLink> projectLinks = [];
    private ProjectsViewModel? watchedProjects;

    /// <summary>Lists the running projects under Projects in the sidebar, keeping the entries already there.</summary>
    private void ListProjects()
    {
        var running = ProjectLooks.SidebarOrder(ProjectRules.Listed(services.Projects.Projects(), showDone: false));
        var links = new List<ProjectLink>();
        foreach (var project in running)
        {
            if (!projectLinks.TryGetValue(project.Id, out var link))
                projectLinks[project.Id] = link = new ProjectLink(ProjectLinkKind.Project, project.Id, project.Name, "");
            link.Title = project.Name;
            link.TileColour = ProjectLooks.ColourOf(project);
            link.TileIcon = ProjectLooks.IconOf(project);
            links.Add(link);
        }
        foreach (var gone in projectLinks.Keys.Except(running.Select(p => p.Id)).ToList())
            projectLinks.Remove(gone);
        Sidebar.ShowLinks(Page.Projects, [myTasksLink], [.. links, templatesLink]);
        OnPropertyChanged(nameof(SelectedNav));
    }

    /// <summary>The sidebar entry for what the Projects page on screen shows, if it is one of the links.</summary>
    private ProjectLink? ShownProjectLink()
    {
        if (Selected?.Key != Page.Projects || Current?.Page is not ProjectsViewModel { Showing: { } showing })
            return null;
        var link = showing switch
        {
            ProjectsViewModel.ShowingMine => myTasksLink,
            ProjectsViewModel.ShowingTemplates => templatesLink,
            _ => projectLinks.GetValueOrDefault(showing),
        };
        // Only one the sidebar shows can be marked; in a folded group the page itself stays picked.
        return link is not null && Sidebar.Shown.Contains(link) ? link : null;
    }

    private void OpenProjectLink(ProjectLink link)
    {
        Selected = this[Page.Projects];
        if (Current.Page is not ProjectsViewModel projects)
            return;
        switch (link.Kind)
        {
            case ProjectLinkKind.MyTasks:
                projects.ShowMine();
                break;
            case ProjectLinkKind.Templates:
                projects.ShowTemplates();
                break;
            default:
                projects.OpenProject(link.Id!);
                break;
        }
    }

    /// <summary>Follows what the Projects page on screen shows, to mark its entry; called whenever the page changes.</summary>
    private void WatchProjects(PageHost host)
    {
        if (watchedProjects is not null)
            watchedProjects.PropertyChanged -= ProjectsShowingChanged;
        watchedProjects = host.Module.Key == Page.Projects ? host.Page as ProjectsViewModel : null;
        if (watchedProjects is not null)
            watchedProjects.PropertyChanged += ProjectsShowingChanged;
    }

    private void ProjectsShowingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectsViewModel.Showing))
            OnPropertyChanged(nameof(SelectedNav));
    }
}
