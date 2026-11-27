using Webignite.Core.Projects;

namespace Webignite.Core.Clients;

/// <summary>A client's projects in the Planner, for their page: the ones still going first, the soonest due at the top.</summary>
public static class ClientPlanner
{
    public static IReadOnlyList<Project> For(IEnumerable<Project> projects, string clientId) =>
        [.. projects.Where(p => p.Deleted is null && p.ClientId == clientId)
            .OrderBy(p => p.Status switch { ProjectStatus.Active => 0, ProjectStatus.OnHold => 1, _ => 2 })
            .ThenBy(p => p.Due ?? DateOnly.MaxValue)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>"Due 12 Dec, late", "On hold" or "Done", with the date written by <paramref name="day"/>.</summary>
    public static string Line(Project project, DateOnly today, Func<DateOnly, string> day) => project.Status switch
    {
        ProjectStatus.Done => "Done",
        ProjectStatus.OnHold => "On hold",
        _ when project.Due is { } due && due < today => $"Due {day(due)}, late",
        _ when project.Due is { } due => $"Due {day(due)}",
        _ => "Under way",
    };

    /// <summary>"Website for Karoo Coffee" when the project says who it's for, else its name.</summary>
    public static string Title(Project project) =>
        string.IsNullOrWhiteSpace(project.For) ? project.Name : $"{project.Name} for {project.For.Trim()}";

    /// <summary>
    /// A client's projects as time logging and invoicing see them, made from their projects in the Planner (one list
    /// for everything). Projects from the old list on the client that haven't been moved across yet are kept too.
    /// </summary>
    public static IReadOnlyList<ClientProject> ForTime(Client client, IEnumerable<Project> projects)
    {
        var planner = projects.Where(p => p.Deleted is null && p.ClientId == client.Id)
            .Select(p => new ClientProject { Id = p.Id, Name = Title(p), Finished = p.Status == ProjectStatus.Done, HourlyRate = p.HourlyRate })
            .ToList();
        var known = planner.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        return [.. planner, .. client.Projects.Where(p => !known.Contains(p.Id))];
    }

    /// <summary>The client with <see cref="Client.Projects"/> as <see cref="ForTime"/> gives, for the time rules.</summary>
    public static Client WithPlannerProjects(Client client, IEnumerable<Project> projects) => client with { Projects = ForTime(client, projects) };

    /// <summary>
    /// Projects on the old list on a client that aren't in the Planner yet, as Planner projects to add: same id (so
    /// the time logged against them stays theirs), name, rate, finished as Done, and shared like the client is. One
    /// deleted in the Planner since isn't brought back.
    /// </summary>
    public static IReadOnlyList<Project> ToMerge(IEnumerable<Client> clients, IEnumerable<Project> everyProject)
    {
        var known = everyProject.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        return [.. clients.SelectMany(c => c.Projects.Where(p => !known.Contains(p.Id) && p.Name.Trim().Length > 0).Select(p => new Project
        {
            Id = p.Id, Name = p.Name.Trim(), ClientId = c.Id, HourlyRate = p.HourlyRate, Shared = true,
            Status = p.Finished ? ProjectStatus.Done : ProjectStatus.Active,
        }))];
    }

    /// <summary>True when a project still going is past its due date.</summary>
    public static bool IsLate(Project project, DateOnly today) =>
        project.Status != ProjectStatus.Done && project.Due is { } due && due < today;
}
