using Webignite.Core.Clients;
using Webignite.Core.Projects;

namespace Webignite.Core.Mail;

/// <summary>A client's name and their projects still going, for filing mail under.</summary>
public sealed record ProjectGroup(string Client, IReadOnlyList<Project> Projects);

/// <summary>
/// Mail filed under a project: the mail stays where it is, and the project is kept on the mail server as an IMAP
/// keyword (as categories are), so it follows the mail to the other PC. The keyword holds the project's id, so a
/// renamed project keeps its mail.
/// </summary>
public static class MailProjects
{
    private const string Prefix = "wi_project_";

    public static string Keyword(string projectId) => Prefix + projectId;

    /// <summary>The project id a keyword stands for, or null when it isn't a project's.</summary>
    public static string? ProjectId(string keyword) =>
        keyword.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && keyword.Length > Prefix.Length ? keyword[Prefix.Length..] : null;

    /// <summary>The ids of the projects a mail is filed under.</summary>
    public static IReadOnlyList<string> Of(MailSummary mail) =>
        [.. mail.Keywords.Select(ProjectId).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Projects still going, grouped under their client by name, the clients in alphabetical order and each one's
    /// projects as the client's page lists them. Projects for no client come last, under "Not for a client".
    /// </summary>
    public static IReadOnlyList<ProjectGroup> Groups(IEnumerable<Project> projects, IEnumerable<Client> clients)
    {
        var names = clients.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        var going = projects.Where(p => p.Deleted is null && p.Status != ProjectStatus.Done).ToList();
        var groups = going.Where(p => p.ClientId is { } id && names.ContainsKey(id))
            .GroupBy(p => p.ClientId!)
            .Select(g => new ProjectGroup(names[g.Key], ClientPlanner.For(g, g.Key)))
            .OrderBy(g => g.Client, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var loose = going.Where(p => p.ClientId is not { } id || !names.ContainsKey(id))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (loose.Count > 0)
            groups.Add(new ProjectGroup("Not for a client", loose));
        return groups;
    }
}
