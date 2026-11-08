using Webignite.Core.Projects;

namespace Webignite.Data;

/// <summary>
/// Projects, their headings and their tasks (subtasks too). Everything but the id is encrypted. Deleted ones stay
/// as markers, with who deleted them and when, so the deletion reaches the other PC when shared projects sync.
/// </summary>
public sealed partial class LocalStore
{
    private const string ProjectContext = "project:";
    private const string HeadingContext = "project-heading:";
    private const string ProjectTaskContext = "project-task:";

    public Project SaveProject(Project project) => Put("projects", ProjectContext, project.Id, project);

    public Project? GetProject(string id) => Find<Project>("projects", ProjectContext, id);

    public IReadOnlyList<Project> ListProjects() => ReadAll<Project>(ProjectContext, "SELECT id, payload FROM projects");

    public Heading SaveHeading(Heading heading) => Put("project_headings", HeadingContext, heading.Id, heading);

    public Heading? GetHeading(string id) => Find<Heading>("project_headings", HeadingContext, id);

    public IReadOnlyList<Heading> ListHeadings() => ReadAll<Heading>(HeadingContext, "SELECT id, payload FROM project_headings");

    public ProjectTask SaveProjectTask(ProjectTask task) => Put("project_tasks", ProjectTaskContext, task.Id, task);

    public ProjectTask? GetProjectTask(string id) => Find<ProjectTask>("project_tasks", ProjectTaskContext, id);

    public IReadOnlyList<ProjectTask> ListProjectTasks() => ReadAll<ProjectTask>(ProjectTaskContext, "SELECT id, payload FROM project_tasks");

    /// <summary>Several saves that must land together, such as a project and everything in it being deleted.</summary>
    public void SaveProjectWork(IEnumerable<Project> projects, IEnumerable<Heading> headings, IEnumerable<ProjectTask> tasks) => InOneGo(() =>
    {
        foreach (var project in projects)
            SaveProject(project);
        foreach (var heading in headings)
            SaveHeading(heading);
        foreach (var task in tasks)
            SaveProjectTask(task);
        return 0;
    });
}
