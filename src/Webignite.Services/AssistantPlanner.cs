using System.Globalization;
using Webignite.Core.Assistants;
using Webignite.Core.Clients;
using Webignite.Core.Projects;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// What Claude and ChatGPT can do in Planner, for Eddie: read projects and tasks, start projects (from a template
/// too), add and change tasks, headings, fields, dependencies, links and status updates, and comment. They act as
/// Eddie, through the same rules as the app. They never delete anything; a problem comes back as an
/// <see cref="ArgumentException"/> in plain words. Every change leaves a note so the open app shows it.
/// </summary>
public sealed class AssistantPlanner
{
    private readonly LocalStore store;
    private readonly TimeProvider clock;
    private readonly ProjectService projects;

    public AssistantPlanner(LocalStore store, TimeProvider? clock = null, string? attachmentsFolder = null)
    {
        this.store = store;
        this.clock = clock ?? TimeProvider.System;
        projects = new ProjectService(store, () => Team().Me!.Id, this.clock, attachmentsFolder);
        projects.Changed += _ => AssistantSignal.Touch(store, "projects", this.clock);
    }

    public sealed record ProjectLine(string Id, string Name, string? Client, string Status, string Health, string? Due, bool Shared,
        int OpenTasks, int DoneTasks, int LateTasks);

    public sealed record TaskLine(string Id, string Title, string Project, string? Heading, string? Assignee, string? Start, string? Due,
        bool Done, string Priority, IReadOnlyList<string> Tags, bool Milestone, IReadOnlyList<string> WaitingOn, string? Subtasks,
        IReadOnlyDictionary<string, string> Fields, string? ParentTask);

    public sealed record FieldLine(string Id, string Name, string Kind, IReadOnlyList<string> Options);

    public sealed record LinkLine(string Title, string Url);

    public sealed record UpdateLine(string Health, string Text, string By, string When);

    public sealed record ProjectDetail(ProjectLine Project, string Description, IReadOnlyList<string> Headings, IReadOnlyList<FieldLine> Fields,
        IReadOnlyList<LinkLine> Links, UpdateLine? LatestUpdate, IReadOnlyList<TaskLine> Tasks);

    public sealed record CommentLine(string By, string When, string Text);

    public sealed record TaskDetail(TaskLine Task, string Notes, IReadOnlyList<TaskLine> Subtasks, IReadOnlyList<string> HoldingUp,
        IReadOnlyList<CommentLine> Comments, IReadOnlyList<string> Files);

    public sealed record TemplateLine(string Id, string Name, string Kind, string About);

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private TeamSettings Team()
    {
        var team = store.GetSetting<TeamSettings>("team") ?? new TeamSettings();
        if (team.Me is not null && team.Partner is not null)
            return team;
        team = ProjectRules.Ensure(team, null);
        store.SetSetting("team", team);
        return team;
    }

    private string? NameOf(string? id)
    {
        var team = Team();
        return id is null ? null : id == team.Me!.Id ? team.Me.Name : id == team.Partner!.Id ? team.Partner.Name : null;
    }

    // Reading

    public IReadOnlyList<ProjectLine> ListProjects(bool includeDone = false) =>
        [.. ProjectRules.Listed(projects.Projects(), includeDone).Select(Line)];

    public ProjectDetail GetProject(string project)
    {
        var found = FindProject(project);
        var tasks = projects.AllTasks(found.Id);
        var team = Team();
        return new ProjectDetail(
            Line(found),
            found.Notes,
            [.. projects.Headings(found.Id).Select(h => h.Name)],
            [.. found.CustomFields.Select(f => new FieldLine(f.Id, f.Name, CustomFields.Label(f.Kind), [.. f.Options.Select(o => o.Name)]))],
            [.. found.Links.Select(l => new LinkLine(l.Title, l.Url))],
            found.Updates.FirstOrDefault() is { } update
                ? new UpdateLine(ProjectLooks.Label(update.Health), update.Text, NameOf(update.By) ?? "Someone", update.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                : null,
            [.. tasks.Where(t => t.ParentId is null).Select(t => Line(t, found, tasks))]);
    }

    /// <summary>Tasks across the projects still going, or one project's; given to me, my partner, no one or anyone.</summary>
    public IReadOnlyList<TaskLine> ListTasks(string? project = null, string? assignee = null, bool includeDone = false, string? dueBefore = null, int limit = 100)
    {
        var (given, before, problem) = AssistantLookup.Date(dueBefore, "due_before");
        if (problem.Length > 0)
            throw new ArgumentException(problem);
        var inScope = project is { Length: > 0 } ? [FindProject(project)] : ProjectRules.Listed(projects.Projects(), false);
        var who = Assignee(assignee, allowAnyone: true);
        return [.. inScope.SelectMany(p =>
            {
                var tasks = projects.AllTasks(p.Id);
                return tasks.Where(t => (includeDone || !t.Done) && (who.Anyone || t.AssignedTo == who.Id) && (!given || t.Due <= before))
                    .Select(t => Line(t, p, tasks));
            })
            .OrderBy(t => t.Due ?? "9999").ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))];
    }

    public TaskDetail GetTask(string task, string? project = null)
    {
        var found = FindTask(task, project);
        var owner = projects.Get(found.ProjectId)!;
        var tasks = projects.AllTasks(owner.Id);
        return new TaskDetail(
            Line(found, owner, tasks),
            found.Notes,
            [.. tasks.Where(t => t.ParentId == found.Id).Select(t => Line(t, owner, tasks))],
            [.. TaskDependencies.Blocking(found, tasks).Select(t => t.Title)],
            [.. projects.Comments(found.Id).Select(c => new CommentLine(NameOf(c.Created.By) ?? "Someone", c.Created.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), c.Text))],
            [.. found.Attachments.Select(a => a.FileName)]);
    }

    public IReadOnlyList<TemplateLine> ListTemplates() =>
        [.. (store.GetSetting<List<ProjectTemplate>>("project-templates") ?? []).Select(t => new TemplateLine(t.Id, t.Name, "Eddie's own", $"{t.Tasks.Count} tasks")),
            .. BuiltInTemplates.All.Select(t => new TemplateLine(t.Id, t.Name, t.Category, t.Description))];

    // Projects

    /// <summary>A new project, blank or from a template (Eddie's own or one that comes with the app), starting on a day.</summary>
    public ProjectDetail CreateProject(string name, string? template = null, string? client = null, string? due = null, bool shared = false,
        string? description = null, string? start = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Give the project a name.");
        var (dueGiven, dueDate, dueProblem) = AssistantLookup.Date(due, "due");
        var (_, startDate, startProblem) = AssistantLookup.Date(start, "start");
        if (dueProblem.Length > 0 || startProblem.Length > 0)
            throw new ArgumentException(dueProblem.Length > 0 ? dueProblem : startProblem);
        var clientId = client is { Length: > 0 } ? FindClient(client).Id : null;
        Project made;
        if (template is { Length: > 0 })
        {
            var own = store.GetSetting<List<ProjectTemplate>>("project-templates") ?? [];
            var choices = own.Select(t => (Template: t, Look: (BuiltInTemplate?)null))
                .Concat(BuiltInTemplates.All.Select(b => (b.Template, Look: (BuiltInTemplate?)b))).ToList();
            var (picked, problem) = AssistantLookup.Find(choices.Select(c => new Picked(c.Template, c.Look)), template, p => p.Template.Id, p => p.Template.Name, "template");
            if (picked is null)
                throw new ArgumentException(problem);
            made = projects.AddFromTemplate(picked.Template, name, startDate ?? Today);
            if (picked.Look is { } look)
                made = projects.Change(made.Id, p => p with { Icon = look.Icon, Colour = look.Colour }).Saved ?? made;
        }
        else
        {
            var (saved, problems) = projects.Add(new Project { Id = ProjectRules.NewId(), Name = name });
            made = saved ?? throw new ArgumentException(string.Join(" ", problems));
        }
        var changed = projects.Change(made.Id, p => p with
        {
            ClientId = clientId ?? p.ClientId,
            Due = dueGiven ? dueDate : p.Due,
            Shared = shared,
            Notes = string.IsNullOrWhiteSpace(description) ? p.Notes : description.Trim(),
            NotesHtml = string.IsNullOrWhiteSpace(description) ? p.NotesHtml : "",
        });
        return GetProject((changed.Saved ?? made).Id);
    }

    /// <summary>Changes what's given about a project; what's left out stays as it is.</summary>
    public ProjectDetail UpdateProject(string project, string? name = null, string? client = null, string? due = null, string? status = null,
        bool? shared = null, string? description = null)
    {
        var found = FindProject(project);
        var (dueGiven, dueDate, dueProblem) = AssistantLookup.Date(due, "due");
        if (dueProblem.Length > 0)
            throw new ArgumentException(dueProblem);
        string? clientId = found.ClientId;
        if (client is { Length: > 0 })
            clientId = client.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : FindClient(client).Id;
        ProjectStatus? newStatus = status switch
        {
            null or "" => null,
            _ when status.Equals("active", StringComparison.OrdinalIgnoreCase) => ProjectStatus.Active,
            _ when status.Replace(" ", "").Equals("onhold", StringComparison.OrdinalIgnoreCase) => ProjectStatus.OnHold,
            _ when status.Equals("done", StringComparison.OrdinalIgnoreCase) => ProjectStatus.Done,
            _ => throw new ArgumentException("status is active, on hold or done."),
        };
        var (saved, problems) = projects.Change(found.Id, p => p with
        {
            Name = string.IsNullOrWhiteSpace(name) ? p.Name : name.Trim(),
            ClientId = clientId,
            Due = dueGiven ? dueDate : p.Due,
            Status = newStatus ?? p.Status,
            Shared = shared ?? p.Shared,
            Notes = description is null ? p.Notes : description.Trim(),
            NotesHtml = description is null ? p.NotesHtml : "",
        });
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetProject(saved.Id);
    }

    public ProjectDetail PostStatus(string project, string health, string text)
    {
        var found = FindProject(project);
        var picked = health.Replace(" ", "").ToLowerInvariant() switch
        {
            "ontrack" => ProjectHealth.OnTrack,
            "atrisk" => ProjectHealth.AtRisk,
            "offtrack" => ProjectHealth.OffTrack,
            _ => throw new ArgumentException("health is on track, at risk or off track."),
        };
        var (saved, problems) = projects.PostStatus(found.Id, picked, text);
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetProject(saved.Id);
    }

    public ProjectDetail AddLink(string project, string url, string? title = null)
    {
        var found = FindProject(project);
        var (saved, problems) = projects.AddLink(found.Id, title ?? "", url);
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetProject(saved.Id);
    }

    public ProjectDetail AddHeading(string project, string name)
    {
        var found = FindProject(project);
        var (saved, problems) = projects.AddHeading(found.Id, name);
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetProject(found.Id);
    }

    public ProjectDetail RenameHeading(string project, string heading, string newName)
    {
        var found = FindProject(project);
        var picked = FindHeading(found.Id, heading);
        var (saved, problems) = projects.ChangeHeading(picked.Id, h => h with { Name = newName });
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetProject(found.Id);
    }

    /// <summary>A custom field on the project: a choice (with options), text, a number or a date.</summary>
    public ProjectDetail AddField(string project, string name, string kind, IReadOnlyList<string>? options = null)
    {
        var found = FindProject(project);
        var picked = kind.Trim().ToLowerInvariant() switch
        {
            "choice" or "list" or "dropdown" => FieldKind.Choice,
            "text" => FieldKind.Text,
            "number" => FieldKind.Number,
            "date" => FieldKind.Date,
            _ => throw new ArgumentException("kind is choice, text, number or date."),
        };
        var (saved, problems) = projects.AddField(found.Id, name, picked, options ?? []);
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetProject(found.Id);
    }

    // Tasks

    /// <summary>A new task in a project (under a heading, or as a subtask of another), given to someone and dated as asked.</summary>
    public TaskDetail AddTask(string project, string title, string? heading = null, string? assignee = null, string? due = null, string? start = null,
        string? notes = null, string? priority = null, IReadOnlyList<string>? tags = null, string? parentTask = null, bool milestone = false)
    {
        var found = FindProject(project);
        var parent = parentTask is { Length: > 0 } ? FindTask(parentTask, found.Id) : null;
        var headingId = parent?.HeadingId ?? (heading is { Length: > 0 } ? FindHeading(found.Id, heading).Id : null);
        var (_, dueDate, dueProblem) = AssistantLookup.Date(due, "due");
        var (_, startDate, startProblem) = AssistantLookup.Date(start, "start");
        if (dueProblem.Length > 0 || startProblem.Length > 0)
            throw new ArgumentException(dueProblem.Length > 0 ? dueProblem : startProblem);
        var who = Assignee(assignee, allowAnyone: false);
        var (saved, problems) = projects.AddTask(new ProjectTask
        {
            Id = ProjectRules.NewId(),
            ProjectId = found.Id,
            HeadingId = headingId,
            ParentId = parent?.Id,
            Title = title,
            Notes = notes?.Trim() ?? "",
            AssignedTo = who.Id,
            Due = dueDate,
            Start = startDate,
            Priority = Priority(priority) ?? TaskPriority.Normal,
            Tags = tags is null ? [] : ProjectRules.Tags(string.Join(",", tags), ProjectRules.AllTags(projects.AllTasks(found.Id))),
            Milestone = milestone,
        });
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetTask(saved.Id);
    }

    /// <summary>Changes what's given about a task (ticking it off too); what's left out stays as it is.</summary>
    public TaskDetail UpdateTask(string task, string? project = null, string? title = null, string? notes = null, string? heading = null,
        string? assignee = null, string? due = null, string? start = null, string? priority = null, IReadOnlyList<string>? tags = null,
        bool? done = null, bool? milestone = null, string? moveToProject = null)
    {
        var found = FindTask(task, project);
        var (dueGiven, dueDate, dueProblem) = AssistantLookup.Date(due, "due");
        var (startGiven, startDate, startProblem) = AssistantLookup.Date(start, "start");
        if (dueProblem.Length > 0 || startProblem.Length > 0)
            throw new ArgumentException(dueProblem.Length > 0 ? dueProblem : startProblem);
        if (moveToProject is { Length: > 0 })
        {
            var target = FindProject(moveToProject);
            if (target.Id != found.ProjectId)
            {
                var (moved, moveProblems) = projects.MoveTask(found.Id, target.Id);
                found = moved ?? throw new ArgumentException(string.Join(" ", moveProblems));
            }
        }
        string? headingId = found.HeadingId;
        if (heading is { Length: > 0 })
            headingId = heading.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : FindHeading(found.ProjectId, heading).Id;
        string? assignedTo = found.AssignedTo;
        if (assignee is { Length: > 0 })
            assignedTo = Assignee(assignee, allowAnyone: false).Id;
        var picked = Priority(priority);
        var known = ProjectRules.AllTags(projects.AllTasks(found.ProjectId));
        var (saved, problems) = projects.ChangeTask(found.Id, t => t with
        {
            Title = string.IsNullOrWhiteSpace(title) ? t.Title : title.Trim(),
            Notes = notes is null ? t.Notes : notes.Trim(),
            NotesHtml = notes is null ? t.NotesHtml : "",
            HeadingId = headingId,
            AssignedTo = assignedTo,
            Due = dueGiven ? dueDate : t.Due,
            Start = startGiven ? startDate : t.Start,
            Priority = picked ?? t.Priority,
            Tags = tags is null ? t.Tags : ProjectRules.Tags(string.Join(",", tags), known),
            Done = done ?? t.Done,
            Milestone = milestone ?? t.Milestone,
        });
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetTask(saved.Id);
    }

    /// <summary>A comment on a task, signed so Eddie can tell who wrote it.</summary>
    public TaskDetail AddComment(string task, string text, string from, string? project = null)
    {
        var found = FindTask(task, project);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Write the comment first.");
        var (_, problem) = projects.AddComment(found.Id, $"{text.Trim()}\n({from})");
        if (problem is not null)
            throw new ArgumentException(problem);
        AssistantSignal.Touch(store, "projects", clock);
        return GetTask(found.Id);
    }

    public TaskDetail SetField(string task, string field, string value, string? project = null)
    {
        var found = FindTask(task, project);
        var owner = projects.Get(found.ProjectId)!;
        var (picked, problem) = AssistantLookup.Find(owner.CustomFields, field, f => f.Id, f => f.Name, "field");
        if (picked is null)
            throw new ArgumentException(problem + " Add it with add_custom_field first.");
        var (saved, problems) = projects.SetFieldValue(found.Id, picked.Id, value ?? "", CultureInfo.InvariantCulture);
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetTask(saved.Id);
    }

    /// <summary>Makes a task wait on another in its project, or stop waiting (which deletes nothing).</summary>
    public TaskDetail SetDependency(string task, string waitsOn, bool waits = true, string? project = null)
    {
        var found = FindTask(task, project);
        var blocker = FindTask(waitsOn, found.ProjectId);
        var (saved, problems) = waits ? projects.AddDependency(found.Id, blocker.Id) : projects.RemoveDependency(found.Id, blocker.Id);
        return saved is null ? throw new ArgumentException(string.Join(" ", problems)) : GetTask(saved.Id);
    }

    // Finding things

    private sealed record Picked(ProjectTemplate Template, BuiltInTemplate? Look);

    private Project FindProject(string typed)
    {
        var (found, problem) = AssistantLookup.Find(projects.Projects(), typed, p => p.Id, p => p.Name, "project");
        return found ?? throw new ArgumentException(problem);
    }

    private ProjectTask FindTask(string typed, string? project)
    {
        var scope = project is { Length: > 0 }
            ? projects.AllTasks(projects.Get(project)?.Id ?? FindProject(project).Id)
            : [.. projects.Projects().SelectMany(p => projects.AllTasks(p.Id))];
        if (projects.GetTask(typed.Trim()) is { } byId)
            return byId;
        var (found, problem) = AssistantLookup.Find(scope, typed, t => t.Id, t => t.Title, "task");
        return found ?? throw new ArgumentException(problem + (project is null ? " Naming the project helps." : ""));
    }

    private Heading FindHeading(string projectId, string typed)
    {
        var (found, problem) = AssistantLookup.Find(projects.Headings(projectId), typed, h => h.Id, h => h.Name, "heading");
        return found ?? throw new ArgumentException(problem + " Add it with add_heading first.");
    }

    private Client FindClient(string typed)
    {
        var clients = store.ListClients();
        if (ClientDirectory.Find(clients, typed) is { } exact)
            return exact;
        var (found, problem) = AssistantLookup.Find(clients, typed, c => c.Id, c => c.Name, "client");
        return found ?? throw new ArgumentException(problem);
    }

    /// <summary>"me", "partner", a name, "no one"; with <paramref name="allowAnyone"/>, empty or "anyone" means anyone.</summary>
    private (bool Anyone, string? Id) Assignee(string? typed, bool allowAnyone)
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0 || text.Equals("anyone", StringComparison.OrdinalIgnoreCase))
            return allowAnyone ? (true, null) : (false, null);
        var team = Team();
        if (text.Equals("me", StringComparison.OrdinalIgnoreCase) || text.Equals("eddie", StringComparison.OrdinalIgnoreCase)
            || text.Equals(team.Me!.Name, StringComparison.OrdinalIgnoreCase))
            return (false, team.Me!.Id);
        if (text.Equals("partner", StringComparison.OrdinalIgnoreCase) || text.Equals(team.Partner!.Name, StringComparison.OrdinalIgnoreCase))
            return (false, team.Partner!.Id);
        if (text.Equals("no one", StringComparison.OrdinalIgnoreCase) || text.Equals("nobody", StringComparison.OrdinalIgnoreCase)
            || text.Equals("none", StringComparison.OrdinalIgnoreCase))
            return (false, null);
        throw new ArgumentException($"assignee is me, partner ({team.Partner!.Name}), or no one.");
    }

    private static TaskPriority? Priority(string? typed) => (typed ?? "").Trim().ToLowerInvariant() switch
    {
        "" => null,
        "high" => TaskPriority.High,
        "normal" or "medium" => TaskPriority.Normal,
        "low" => TaskPriority.Low,
        _ => throw new ArgumentException("priority is high, normal or low."),
    };

    // Shaping what goes back

    private ProjectLine Line(Project project)
    {
        var tasks = projects.AllTasks(project.Id).Where(t => t.ParentId is null).ToList();
        var client = project.ClientId is { } id ? store.GetClient(id)?.Name : null;
        return new ProjectLine(project.Id, project.Name, client, ProjectRules.Label(project.Status), project.Health == ProjectHealth.None ? "Not set" : ProjectLooks.Label(project.Health),
            AssistantLookup.Day(project.Due), project.Shared, tasks.Count(t => !t.Done), tasks.Count(t => t.Done),
            tasks.Count(t => !t.Done && t.Due < Today));
    }

    private TaskLine Line(ProjectTask task, Project project, IReadOnlyList<ProjectTask> tasks)
    {
        var headings = projects.Headings(project.Id);
        return new TaskLine(task.Id, task.Title, project.Name, headings.FirstOrDefault(h => h.Id == task.HeadingId)?.Name, NameOf(task.AssignedTo),
            AssistantLookup.Day(task.Start), AssistantLookup.Day(task.Due), task.Done, ProjectRules.Label(task.Priority), task.Tags, task.Milestone,
            [.. TaskDependencies.BlockedBy(task, tasks).Where(t => !t.Done).Select(t => t.Title)],
            task.ParentId is null ? ProjectRules.SubtaskCount(tasks.Where(t => t.ParentId == task.Id)) is { Length: > 0 } count ? count : null : null,
            project.CustomFields.Where(f => task.Values.ContainsKey(f.Id))
                .ToDictionary(f => f.Name, f => CustomFields.Show(f, task.Values[f.Id], CultureInfo.InvariantCulture)),
            task.ParentId is { } parent ? tasks.FirstOrDefault(t => t.Id == parent)?.Title : null);
    }
}
