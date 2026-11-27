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

    /// <summary>True when a project still going is past its due date.</summary>
    public static bool IsLate(Project project, DateOnly today) =>
        project.Status != ProjectStatus.Done && project.Due is { } due && due < today;
}
