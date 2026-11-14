using Webignite.Core.Clients;
using Webignite.Core.Projects;

namespace Webignite.Core.Studio;

/// <summary>A stop on a project's line: one of its headings, with how many of its tasks are done.</summary>
public sealed record LineStop(string Name, int Done, int Total)
{
    public bool Finished => Total > 0 && Done == Total;
}

/// <summary>
/// One project drawn as a line with a stop for each heading. <paramref name="Position"/> is how far along it is,
/// counted in stops: 0 at the first, 1.5 halfway between the second and third, the last stop's number when done.
/// </summary>
public sealed record ProjectLine(string ProjectId, string Name, string Client, string Colour, IReadOnlyList<LineStop> Stops, double Position, ProjectHealth Health, string Detail);

/// <summary>
/// Studio's lines view: each running project as a line on an underground map, a stop for each of its headings in
/// order, and a marker where the work has got to.
/// </summary>
public static class StudioLines
{
    /// <summary>The stop that tasks under no heading go to, first on the line.</summary>
    public const string NoHeading = "Start";

    /// <summary>The stop added at the end of every line: where the marker rests once every task is done.</summary>
    public const string Finish = "Done";

    /// <summary>
    /// A line for each running project with at least one task, the ones furthest along first. Only top-level tasks
    /// count.
    /// </summary>
    public static IReadOnlyList<ProjectLine> Build(IEnumerable<Project> projects, IEnumerable<Heading> headings, IEnumerable<ProjectTask> tasks,
        IEnumerable<Client> clients)
    {
        var byProject = tasks.Where(t => t.Deleted is null && t.ParentId is null).ToLookup(t => t.ProjectId);
        var stops = headings.Where(h => h.Deleted is null).OrderBy(h => h.Order).ToLookup(h => h.ProjectId);
        var owners = clients.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(c => c.Id, c => c.Name);
        var lines = new List<ProjectLine>();
        foreach (var project in projects.Where(p => p.Deleted is null && p.Status != ProjectStatus.Done).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var mine = byProject[project.Id].ToList();
            if (mine.Count == 0)
                continue;
            var made = new List<LineStop>();
            var loose = mine.Where(t => t.HeadingId is null || stops[project.Id].All(h => h.Id != t.HeadingId)).ToList();
            if (loose.Count > 0)
                made.Add(new LineStop(NoHeading, loose.Count(t => t.Done), loose.Count));
            foreach (var heading in stops[project.Id])
            {
                var under = mine.Where(t => t.HeadingId == heading.Id).ToList();
                // A heading with nothing under it yet is still a stop: the plan shows even before the tasks do.
                made.Add(new LineStop(heading.Name, under.Count(t => t.Done), under.Count));
            }
            made.Add(new LineStop(Finish, 0, 0));
            var done = mine.Count(t => t.Done);
            lines.Add(new ProjectLine(project.Id, project.Name, project.ClientId is { } id ? owners.GetValueOrDefault(id) ?? "" : "",
                ProjectLooks.ColourOf(project), made, PositionOf(made), project.Health,
                done == mine.Count ? "Everything done" : $"{done} of {mine.Count} tasks done"));
        }
        return [.. lines.OrderByDescending(l => l.Stops.Count <= 1 ? 0 : l.Position / (l.Stops.Count - 1)).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// How far along the stops the work is: past every stop that is finished (or has nothing to do) up to the
    /// first that isn't, plus the share of that one that is done. The last stop, Done, is reached only when all are.
    /// </summary>
    public static double PositionOf(IReadOnlyList<LineStop> stops)
    {
        for (var i = 0; i < stops.Count - 1; i++)
        {
            var stop = stops[i];
            if (stop.Total > 0 && stop.Done < stop.Total)
                return i + (double)stop.Done / stop.Total;
        }
        return Math.Max(0, stops.Count - 1);
    }
}
