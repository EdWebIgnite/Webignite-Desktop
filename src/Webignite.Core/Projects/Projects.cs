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

/// <summary>How the Projects page shows a project's tasks: as a list, or as a board with a column per heading.</summary>
public sealed record ProjectViewSettings
{
    public bool Board { get; init; }
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

/// <summary>How often a task comes back once it's done.</summary>
public enum TaskRepeat { None, Daily, Weekly, Monthly, Yearly }

/// <summary>
/// A project kept to start others from: its headings and tasks, with subtasks, and each task's dates as days
/// after the project starts. Nobody is given anything in a template; that's decided per project.
/// </summary>
public sealed record ProjectTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Headings { get; init; } = [];
    public IReadOnlyList<TemplateTask> Tasks { get; init; } = [];
}

/// <summary>A task in a template; <see cref="Heading"/> is a heading's name, or null for none.</summary>
public sealed record TemplateTask
{
    public required string Title { get; init; }
    public string? Heading { get; init; }
    public string Notes { get; init; } = "";
    public TaskPriority Priority { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public TaskRepeat Repeat { get; init; }
    public int? StartDay { get; init; }
    public int? DueDay { get; init; }
    public IReadOnlyList<string> Subtasks { get; init; } = [];
}

/// <summary>How much a task matters: most are Normal.</summary>
public enum TaskPriority { Normal, High, Low }

/// <summary>
/// A mail a task is about: where it was, and enough to show it without opening it. The message id finds it
/// again after it's moved to another folder.
/// </summary>
public sealed record TaskMail(string AccountId, string Folder, uint Uid, string? MessageId, string Subject, string? From, DateTimeOffset Date)
{
    public static TaskMail Of(Webignite.Core.Mail.MailSummary mail) =>
        new(mail.AccountId, mail.Folder, mail.Uid, mail.MessageId, mail.Subject, mail.From?.Display, mail.Date);
}

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

    /// <summary>The client it's about, when not just the project's.</summary>
    public string? ClientId { get; init; }

    /// <summary>The quote or invoice it's about.</summary>
    public string? DocumentId { get; init; }

    /// <summary>The mail it's about.</summary>
    public TaskMail? Mail { get; init; }

    public TaskPriority Priority { get; init; }

    /// <summary>Words to group and find tasks by, such as Hosting or SEO.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Ticking it off makes the next one, due a day, week, month or year later.</summary>
    public TaskRepeat Repeat { get; init; }

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

    /// <summary>
    /// Who made something and who changed it last, by name, for the foot of a task: "Made by Eddie on 5 Nov.
    /// Changed by Jaco on 6 Nov at 14:20." Someone not in the team shows as "someone else".
    /// </summary>
    public static string History(ITracked item, IReadOnlyDictionary<string, string> names, TimeZoneInfo zone, IFormatProvider culture)
    {
        string Who(string id) => names.TryGetValue(id, out var name) ? name : "someone else";
        string When(DateTimeOffset at, bool time)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            return string.Format(culture, time ? "{0:d MMM} at {0:HH:mm}" : "{0:d MMM}", local);
        }
        var made = $"Made by {Who(item.Created.By)} on {When(item.Created.At, false)}.";
        return item.Changed == item.Created ? made : $"{made} Changed by {Who(item.Changed.By)} on {When(item.Changed.At, true)}.";
    }

    /// <summary>
    /// The quotes and invoices a task can be about: the client's, newest first, or the newest 100 when no client
    /// is picked.
    /// </summary>
    public static IReadOnlyList<Webignite.Core.Documents.StoredDocument> DocumentsFor(Webignite.Core.Clients.Client? client,
        IEnumerable<Webignite.Core.Documents.StoredDocument> documents)
    {
        var list = documents.OrderByDescending(d => d.Updated);
        return client is null ? [.. list.Take(100)] : [.. list.Where(d => Webignite.Core.Clients.ClientDirectory.Matches(client, d.Document.Customer.Name))];
    }

    /// <summary>"Invoice INV-0107, Karoo Coffee Roasters, R 2 725.00"; a draft without a number says so.</summary>
    public static string DocumentLabel(Webignite.Core.Documents.StoredDocument stored)
    {
        var document = stored.Document;
        var kind = document.Kind == Webignite.Core.Documents.DocumentKind.Quote ? "Quote" : "Invoice";
        var name = document.Number is { Length: > 0 } number ? $"{kind} {number}" : $"Draft {kind.ToLowerInvariant()}";
        var total = document.Total is { } amount ? ", " + Webignite.Core.Documents.Money.Rand(amount) : "";
        return $"{name}, {document.Customer.Name}{total}";
    }

    /// <summary>
    /// My tasks: open tasks and subtasks given to me, in projects that aren't deleted or done.
    /// </summary>
    public static IReadOnlyList<ProjectTask> Mine(IEnumerable<ProjectTask> tasks, IEnumerable<Project> projects, string meId)
    {
        var live = projects.Where(p => p.Deleted is null && p.Status != ProjectStatus.Done).Select(p => p.Id).ToHashSet();
        return [.. tasks.Where(t => t.Deleted is null && !t.Done && t.AssignedTo == meId && live.Contains(t.ProjectId))];
    }

    /// <summary>
    /// My tasks by when they're due: overdue, today, the rest of this week, later, and no date. Within a group,
    /// by date, then High before Normal before Low.
    /// </summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<ProjectTask> Tasks)> ByWhen(IEnumerable<ProjectTask> tasks, DateOnly today)
    {
        var weekEnd = Dates.WeekStart(today).AddDays(6);
        static int Rank(TaskPriority p) => p switch { TaskPriority.High => 0, TaskPriority.Normal => 1, _ => 2 };
        var sorted = tasks.OrderBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => Rank(t.Priority)).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        (string, IReadOnlyList<ProjectTask>)[] groups =
        [
            ("Overdue", sorted.Where(t => t.Due < today).ToList()),
            ("Today", sorted.Where(t => t.Due == today).ToList()),
            ("This week", sorted.Where(t => t.Due > today && t.Due <= weekEnd).ToList()),
            ("Later", sorted.Where(t => t.Due > weekEnd).ToList()),
            ("No due date", sorted.Where(t => t.Due is null).ToList()),
        ];
        return [.. groups.Where(g => g.Item2.Count > 0)];
    }

    /// <summary>My tasks due in a range, for Calendar, Planner and Today: "Wireframes (Bosveld Builders website)".</summary>
    public static IReadOnlyList<Webignite.Core.Calendar.AgendaItem> Agenda(DateOnly from, DateOnly to, IEnumerable<ProjectTask> mine, IReadOnlyDictionary<string, string> projectNames) =>
        [.. mine.Where(t => t.Due is { } due && due >= from && due <= to)
            .Select(t => new Webignite.Core.Calendar.AgendaItem(t.Due!.Value, Webignite.Core.Calendar.AgendaKind.ProjectTask,
                projectNames.TryGetValue(t.ProjectId, out var name) ? $"{t.Title} ({name})" : t.Title, null, t.Id, t.Done))];

    /// <summary>A task made from a mail: its subject as the title, the mail linked, given to me.</summary>
    public static ProjectTask FromMail(Webignite.Core.Mail.MailSummary mail, string projectId, string meId, string? clientId) => new()
    {
        Id = NewId(),
        ProjectId = projectId,
        Title = string.IsNullOrWhiteSpace(mail.Subject) ? $"Mail from {mail.From?.Display ?? "someone"}" : mail.Subject.Trim(),
        AssignedTo = meId,
        ClientId = clientId,
        Mail = TaskMail.Of(mail),
    };

    /// <summary>The note on a timer started from a task: "Wireframes, Bosveld Builders website".</summary>
    public static string TimerNote(ProjectTask task, string? projectName) =>
        string.IsNullOrWhiteSpace(projectName) ? task.Title : $"{task.Title}, {projectName}";

    public static string Label(TaskRepeat repeat) => repeat switch
    {
        TaskRepeat.Daily => "Every day",
        TaskRepeat.Weekly => "Every week",
        TaskRepeat.Monthly => "Every month",
        TaskRepeat.Yearly => "Every year",
        _ => "Doesn't repeat",
    };

    /// <summary>
    /// The next one of a repeating task just ticked off: a fresh copy, due a step after it was due (or after today
    /// when it had no date), its start moved by as much, under the same heading, straight after it.
    /// Null when it doesn't repeat.
    /// </summary>
    public static ProjectTask? Next(ProjectTask done, DateOnly today, double order)
    {
        if (done.Repeat == TaskRepeat.None)
            return null;
        var from = done.Due ?? today;
        var due = Step(from, done.Repeat);
        // Ticked off late: skip ahead so the next one isn't already overdue.
        while (due < today)
            due = Step(due, done.Repeat);
        var shift = due.DayNumber - from.DayNumber;
        return done with
        {
            Id = NewId(),
            Done = false,
            Due = due,
            Start = done.Start?.AddDays(shift),
            Order = order,
            Created = default,
            Changed = default,
            Deleted = null,
            Fields = new Dictionary<string, Stamp>(),
        };
    }

    private static DateOnly Step(DateOnly day, TaskRepeat repeat) => repeat switch
    {
        TaskRepeat.Daily => day.AddDays(1),
        TaskRepeat.Weekly => day.AddDays(7),
        TaskRepeat.Monthly => day.AddMonths(1),
        _ => day.AddYears(1),
    };

    /// <summary>
    /// A project as a template: its headings in order, its tasks with their subtasks' titles, and dates as days
    /// after the earliest date in it. Who tasks were given to, what's done and links to mail or invoices stay behind,
    /// and so do repeating tasks already ticked off (the next one is there).
    /// </summary>
    public static ProjectTemplate TemplateOf(string name, IEnumerable<Heading> headings, IEnumerable<ProjectTask> tasks)
    {
        var live = headings.Where(h => h.Deleted is null).OrderBy(h => h.Order).ToList();
        // A repeating task already ticked off has a next one carrying on, so only that one goes in.
        var all = tasks.Where(t => t.Deleted is null && !(t.Done && t.Repeat != TaskRepeat.None)).ToList();
        var top = Sections(live, all).SelectMany(s => s.Tasks).ToList();
        var dates = top.SelectMany(t => new[] { t.Start, t.Due }).OfType<DateOnly>().ToList();
        var first = dates.Count == 0 ? (DateOnly?)null : dates.Min();
        int? Day(DateOnly? date) => date is { } d && first is { } f ? d.DayNumber - f.DayNumber : null;
        return new ProjectTemplate
        {
            Id = NewId(),
            Name = name.Trim(),
            Headings = [.. live.Select(h => h.Name)],
            Tasks = [.. top.Select(t => new TemplateTask
            {
                Title = t.Title,
                Heading = live.FirstOrDefault(h => h.Id == t.HeadingId)?.Name,
                Notes = t.Notes,
                Priority = t.Priority,
                Tags = t.Tags,
                Repeat = t.Repeat,
                StartDay = Day(t.Start),
                DueDay = Day(t.Due),
                Subtasks = [.. all.Where(s => s.ParentId == t.Id).OrderBy(s => s.Order).Select(s => s.Title)],
            })],
        };
    }

    /// <summary>A new project from a template, starting on a day: its headings, tasks and subtasks, all new.</summary>
    public static (Project Project, IReadOnlyList<Heading> Headings, IReadOnlyList<ProjectTask> Tasks) FromTemplate(
        ProjectTemplate template, string name, DateOnly start, double order)
    {
        var project = new Project { Id = NewId(), Name = string.IsNullOrWhiteSpace(name) ? template.Name : name.Trim(), Order = order };
        var headings = template.Headings.Select((h, i) => new Heading { Id = NewId(), ProjectId = project.Id, Name = h, Order = i + 1 }).ToList();
        var tasks = new List<ProjectTask>();
        var place = 0;
        foreach (var t in template.Tasks)
        {
            var task = new ProjectTask
            {
                Id = NewId(),
                ProjectId = project.Id,
                HeadingId = headings.FirstOrDefault(h => h.Name == t.Heading)?.Id,
                Title = t.Title,
                Notes = t.Notes,
                Priority = t.Priority,
                Tags = t.Tags,
                Repeat = t.Repeat,
                Start = t.StartDay is { } s ? start.AddDays(s) : null,
                Due = t.DueDay is { } d ? start.AddDays(d) : null,
                Order = ++place,
            };
            tasks.Add(task);
            tasks.AddRange(t.Subtasks.Select((title, i) => new ProjectTask
            {
                Id = NewId(), ProjectId = project.Id, HeadingId = task.HeadingId, ParentId = task.Id, Title = title, Order = i + 1,
            }));
        }
        return (project, headings, tasks);
    }

    /// <summary>
    /// When a task is due, as a list shows it: Today, Tomorrow, Yesterday, the day's name within the coming week,
    /// otherwise the date ("12 Nov"), with the year when it isn't this year. Dates follow the PC's region.
    /// </summary>
    public static string DueLabel(DateOnly due, DateOnly today)
    {
        var days = due.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and < 7 => due.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture),
            _ => due.ToString(due.Year == today.Year ? "d MMM" : "d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture),
        };
    }

    public static string Label(TaskPriority priority) => priority switch
    {
        TaskPriority.High => "High",
        TaskPriority.Low => "Low",
        _ => "Normal",
    };

    /// <summary>
    /// Tags as typed ("hosting, SEO,  Hosting") made tidy: split on commas, trimmed, each once whatever its case,
    /// written as a tag already in use is written, else as typed.
    /// </summary>
    public static IReadOnlyList<string> Tags(string? typed, IEnumerable<string> known)
    {
        var existing = known.ToList();
        var tags = new List<string>();
        foreach (var part in (typed ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = existing.FirstOrDefault(k => string.Equals(k, part, StringComparison.OrdinalIgnoreCase)) ?? part;
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                tags.Add(tag);
        }
        return tags;
    }

    /// <summary>Every tag used on these tasks, A to Z.</summary>
    public static IReadOnlyList<string> AllTags(IEnumerable<ProjectTask> tasks) =>
        [.. tasks.Where(t => t.Deleted is null).SelectMany(t => t.Tags).DistinctBy(t => t, StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Whether a task shows under a filter; null means any priority, or any tag.</summary>
    public static bool Shows(ProjectTask task, TaskPriority? priority, string? tag) =>
        (priority is null || task.Priority == priority)
        && (tag is null || task.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    /// <summary>A task added while the list is filtered gets the filter's priority and tag, so it stays in view.</summary>
    public static ProjectTask Filtered(ProjectTask task, TaskPriority? priority, string? tag) => task with
    {
        Priority = priority ?? task.Priority,
        Tags = tag is null || task.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? task.Tags : [.. task.Tags, tag],
    };

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
        ("client", t => t.ClientId), ("document", t => t.DocumentId), ("mail", t => t.Mail), ("priority", t => t.Priority), ("tags", t => t.Tags), ("repeat", t => t.Repeat),
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
