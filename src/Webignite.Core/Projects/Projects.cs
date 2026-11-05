using System.Collections;

namespace Webignite.Core.Projects;

/// <summary>Who did something, and when. Every project, heading and task carries these, so sync can come later.</summary>
public readonly record struct Stamp(string By, DateTimeOffset At);

/// <summary>One of the two people using projects: you, or your business partner.</summary>
public sealed record Teammate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Email { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>You and your partner, set up in Settings. The partner can be renamed any time.</summary>
public sealed record TeamSettings
{
    public Teammate? Me { get; init; }
    public Teammate? Partner { get; init; }
}

/// <summary>
/// What every record in projects has for sync: who made it, who changed it last, who changed each field last,
/// and who deleted it. A deleted record stays (as a marker) so the deletion can reach the other PC.
/// </summary>
public interface ITracked
{
    string Id { get; }
    Stamp Created { get; }
    Stamp Changed { get; }
    Stamp? Deleted { get; }

    /// <summary>Each field's last change, by field name. Later, when both change the same field, the later one wins.</summary>
    IReadOnlyDictionary<string, Stamp> Fields { get; }
}

public enum ProjectStatus { Active, OnHold, Done }

/// <summary>A project: shared with your partner or just yours, optionally for a client.</summary>
public sealed record Project : ITracked
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public bool Shared { get; init; }
    public string? ClientId { get; init; }
    public ProjectStatus Status { get; init; }
    public DateOnly? Due { get; init; }
    public string Notes { get; init; } = "";

    /// <summary>Where it sits in the list of projects; smaller first.</summary>
    public double Order { get; init; }

    public Stamp Created { get; init; }
    public Stamp Changed { get; init; }
    public Stamp? Deleted { get; init; }
    public IReadOnlyDictionary<string, Stamp> Fields { get; init; } = new Dictionary<string, Stamp>();
}

/// <summary>A heading groups tasks in a project (Design, Build, Launch); on the board it's a column.</summary>
public sealed record Heading : ITracked
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public string Name { get; init; } = "";
    public double Order { get; init; }

    public Stamp Created { get; init; }
    public Stamp Changed { get; init; }
    public Stamp? Deleted { get; init; }
    public IReadOnlyDictionary<string, Stamp> Fields { get; init; } = new Dictionary<string, Stamp>();
}

/// <summary>
/// A task in a project, under a heading or none. A subtask is a task with a <see cref="ParentId"/>; subtasks go
/// one level deep only.
/// </summary>
public sealed record ProjectTask : ITracked
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public string? HeadingId { get; init; }
    public string? ParentId { get; init; }
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";

    /// <summary>The teammate it's given to, by id.</summary>
    public string? AssignedTo { get; init; }

    public DateOnly? Start { get; init; }
    public DateOnly? Due { get; init; }
    public bool Done { get; init; }
    public double Order { get; init; }

    public Stamp Created { get; init; }
    public Stamp Changed { get; init; }
    public Stamp? Deleted { get; init; }
    public IReadOnlyDictionary<string, Stamp> Fields { get; init; } = new Dictionary<string, Stamp>();
}

/// <summary>The rules for people, projects, headings and tasks: checks, order, and recording who changed what.</summary>
public static class ProjectRules
{
    public const string DefaultPartner = "Partner";

    public static string NewId() => Guid.CreateVersion7().ToString("N");

    /// <summary>The team with both people in it: you ("Me" unless a name is known) and "Partner", until renamed.</summary>
    public static TeamSettings Ensure(TeamSettings team, string? myName)
    {
        var me = team.Me ?? new Teammate { Id = Teammate.NewId(), Name = string.IsNullOrWhiteSpace(myName) ? "Me" : myName.Trim() };
        var partner = team.Partner ?? new Teammate { Id = Teammate.NewId(), Name = DefaultPartner };
        return team with { Me = me, Partner = partner };
    }

    /// <summary>What's wrong with the people as typed in Settings; empty when fine.</summary>
    public static IReadOnlyList<string> Check(Teammate me, Teammate partner)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(me.Name))
            problems.Add("Type your own name.");
        if (string.IsNullOrWhiteSpace(partner.Name))
            problems.Add("Type your partner's name.");
        else if (string.Equals(me.Name.Trim(), partner.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            problems.Add("Give your partner a different name from yours, so you can tell who did what.");
        if (partner.Email is { Length: > 0 } email && !email.Contains('@', StringComparison.Ordinal))
            problems.Add("Your partner's email address doesn't look right.");
        return problems;
    }

    public static IReadOnlyList<string> Check(Project project) =>
        string.IsNullOrWhiteSpace(project.Name) ? ["Give the project a name."] : [];

    public static IReadOnlyList<string> Check(Heading heading) =>
        string.IsNullOrWhiteSpace(heading.Name) ? ["Give the heading a name."] : [];

    /// <summary>A task needs a title; a subtask's parent must be a task in the same project that isn't a subtask itself.</summary>
    public static IReadOnlyList<string> Check(ProjectTask task, ProjectTask? parent)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(task.Title))
            problems.Add("Give the task a title.");
        if (task.ParentId is not null)
        {
            if (parent is null || parent.Deleted is not null)
                problems.Add("The task this was under is gone.");
            else if (parent.ParentId is not null)
                problems.Add("Subtasks go one level deep: this one can't have its own.");
            else if (parent.ProjectId != task.ProjectId)
                problems.Add("A subtask has to be in the same project as its task.");
        }
        if (task.Start is { } start && task.Due is { } due && due < start)
            problems.Add("The due date is before the start date.");
        return problems;
    }

    /// <summary>The place after the last one, for something added at the end.</summary>
    public static double NextOrder(IEnumerable<double> orders) => orders.DefaultIfEmpty(0).Max() + 1;

    /// <summary>A place between two neighbours, for something dropped between them; either can be missing.</summary>
    public static double Between(double? before, double? after) => (before, after) switch
    {
        (null, null) => 1,
        ({ } b, null) => b + 1,
        (null, { } a) => a - 1,
        ({ } b, { } a) => (b + a) / 2,
    };

    /// <summary>
    /// A project's tasks by heading, for the list: tasks under no heading first (only when there are some, or no
    /// headings at all), then each heading in its order. Subtasks aren't listed; they're counted on their task.
    /// </summary>
    public static IReadOnlyList<(Heading? Heading, IReadOnlyList<ProjectTask> Tasks)> Sections(IEnumerable<Heading> headings, IEnumerable<ProjectTask> tasks)
    {
        var list = headings.Where(h => h.Deleted is null).OrderBy(h => h.Order).ToList();
        var known = list.Select(h => h.Id).ToHashSet();
        var top = tasks.Where(t => t.Deleted is null && t.ParentId is null).OrderBy(t => t.Order).ToList();
        var sections = new List<(Heading?, IReadOnlyList<ProjectTask>)>();
        var loose = top.Where(t => t.HeadingId is null || !known.Contains(t.HeadingId)).ToList();
        if (loose.Count > 0 || list.Count == 0)
            sections.Add((null, loose));
        foreach (var heading in list)
            sections.Add((heading, top.Where(t => t.HeadingId == heading.Id).ToList()));
        return sections;
    }

    /// <summary>
    /// Where a task dropped into a section lands: <paramref name="index"/> is its place among the section's tasks
    /// as shown, counting the task itself if it was already there. Returns the order that puts it there.
    /// </summary>
    public static double PlaceAt(IReadOnlyList<ProjectTask> section, int index, string movingId)
    {
        var others = section.Where(t => t.Id != movingId).ToList();
        var from = section.ToList().FindIndex(t => t.Id == movingId);
        // Dropping below itself counts the gap it leaves.
        if (from >= 0 && index > from)
            index--;
        index = Math.Clamp(index, 0, others.Count);
        return Between(index > 0 ? others[index - 1].Order : null, index < others.Count ? others[index].Order : null);
    }

    /// <summary>"2 of 3" for a task's subtasks; empty when it has none.</summary>
    public static string SubtaskCount(IEnumerable<ProjectTask> subtasks)
    {
        var list = subtasks.Where(t => t.Deleted is null).ToList();
        return list.Count == 0 ? "" : $"{list.Count(t => t.Done)} of {list.Count}";
    }

    /// <summary>The projects for the side list: active and on hold first in their order, done ones last.</summary>
    public static IReadOnlyList<Project> Listed(IEnumerable<Project> projects, bool showDone) =>
        [.. projects.Where(p => p.Deleted is null && (showDone || p.Status != ProjectStatus.Done))
            .OrderBy(p => p.Status == ProjectStatus.Done).ThenBy(p => p.Order)];

    public static string Label(ProjectStatus status) => status switch
    {
        ProjectStatus.OnHold => "On hold",
        ProjectStatus.Done => "Done",
        _ => "Active",
    };

    // Recording changes. Each kind lists its fields once; a field whose value changed gets the new stamp.

    private static readonly (string Name, Func<Project, object?> Value)[] ProjectFields =
    [
        ("name", p => p.Name), ("shared", p => p.Shared), ("client", p => p.ClientId), ("status", p => p.Status),
        ("due", p => p.Due), ("notes", p => p.Notes), ("order", p => p.Order),
    ];

    private static readonly (string Name, Func<Heading, object?> Value)[] HeadingFields =
    [
        ("name", h => h.Name), ("order", h => h.Order),
    ];

    private static readonly (string Name, Func<ProjectTask, object?> Value)[] TaskFields =
    [
        ("heading", t => t.HeadingId), ("parent", t => t.ParentId), ("title", t => t.Title), ("notes", t => t.Notes),
        ("assigned", t => t.AssignedTo), ("start", t => t.Start), ("due", t => t.Due), ("done", t => t.Done), ("order", t => t.Order),
    ];

    /// <summary>The project as saved: who made it, and who changed which field, now.</summary>
    public static Project Stamped(Project? before, Project after, Stamp now)
    {
        var (created, fields, changed) = Mark(before, after, now, ProjectFields);
        return after with { Created = created, Fields = fields, Changed = changed ? now : before!.Changed, Deleted = before?.Deleted };
    }

    public static Heading Stamped(Heading? before, Heading after, Stamp now)
    {
        var (created, fields, changed) = Mark(before, after, now, HeadingFields);
        return after with { Created = created, Fields = fields, Changed = changed ? now : before!.Changed, Deleted = before?.Deleted };
    }

    public static ProjectTask Stamped(ProjectTask? before, ProjectTask after, Stamp now)
    {
        var (created, fields, changed) = Mark(before, after, now, TaskFields);
        return after with { Created = created, Fields = fields, Changed = changed ? now : before!.Changed, Deleted = before?.Deleted };
    }

    private static (Stamp Created, IReadOnlyDictionary<string, Stamp> Fields, bool Changed) Mark<T>(T? before, T after, Stamp now,
        (string Name, Func<T, object?> Value)[] fields) where T : class, ITracked
    {
        if (before is null)
            return (now, fields.ToDictionary(f => f.Name, _ => now), true);
        var stamps = new Dictionary<string, Stamp>(before.Fields);
        var changed = false;
        foreach (var (name, value) in fields)
        {
            if (Same(value(before), value(after)))
                continue;
            stamps[name] = now;
            changed = true;
        }
        return (before.Created, stamps, changed);
    }

    private static bool Same(object? a, object? b) =>
        a is IEnumerable x and not string && b is IEnumerable y and not string
            ? x.Cast<object?>().SequenceEqual(y.Cast<object?>())
            : Equals(a, b);
}
