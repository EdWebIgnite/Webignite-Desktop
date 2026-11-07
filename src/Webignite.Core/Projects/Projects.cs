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

    /// <summary>Who made or changed something, by the id it was stamped with: me, my partner, or Claude or ChatGPT.</summary>
    public string NameOf(string? id) =>
        id is null ? "Someone"
        : id == Me?.Id ? Me.Name
        : id == Partner?.Id ? Partner.Name
        : Actors.Name(id) ?? "Someone";
}

/// <summary>
/// Claude and ChatGPT stamp what they change with their own ids, not Eddie's, so comments, status updates and the
/// activity feed show who really did it.
/// </summary>
public static class Actors
{
    public const string Claude = "assistant-claude";
    public const string ChatGpt = "assistant-chatgpt";

    public static string? Name(string id) => id switch
    {
        Claude => "Claude",
        ChatGpt => "ChatGPT",
        _ => null,
    };

    public static bool IsAssistant(string? id) => id is Claude or ChatGpt;
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

/// <summary>How a project is going, as Asana's "Set status": not set, on track, at risk or off track.</summary>
public enum ProjectHealth { None, OnTrack, AtRisk, OffTrack }

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

    /// <summary>The project's custom fields, with their options; tasks keep their values by these fields' ids.</summary>
    public IReadOnlyList<CustomField> Fields { get; init; } = [];

    /// <summary>
    /// What waits on what, subtasks included, counting each task and then its subtasks in the template's order.
    /// Templates kept before this have none and use each task's <see cref="TemplateTask.WaitsOn"/> instead.
    /// </summary>
    public IReadOnlyList<TemplateWait> Waits { get; init; } = [];
}

/// <summary>
/// A task in a template waiting on another, each by its place counting tasks and subtasks together: a task, then
/// its subtasks, then the next task.
/// </summary>
public sealed record TemplateWait(int Task, int On);

/// <summary>
/// A task in a template. <see cref="HeadingIndex"/> is its heading's place in the template's headings, so two
/// headings with the same name stay apart; templates kept before it find theirs by <see cref="Heading"/>, the
/// heading's name, or null for none.
/// </summary>
public sealed record TemplateTask
{
    public required string Title { get; init; }
    public string? Heading { get; init; }
    public int? HeadingIndex { get; init; }
    public string Notes { get; init; } = "";
    public TaskPriority Priority { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public TaskRepeat Repeat { get; init; }
    public int? StartDay { get; init; }
    public int? DueDay { get; init; }
    public IReadOnlyList<string> Subtasks { get; init; } = [];

    /// <summary>A milestone, shown with a diamond.</summary>
    public bool Milestone { get; init; }

    /// <summary>Its values for the template's custom fields, by field id.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The tasks it waits on, by their place in the template's list of tasks, subtasks not counted. Still written
    /// for templates read by older copies; <see cref="ProjectTemplate.Waits"/> wins when it has any.
    /// </summary>
    public IReadOnlyList<int> WaitsOn { get; init; } = [];
}

/// <summary>A file attached to a task: its name as attached, its size, and who added it when. The file is kept on this PC.</summary>
public sealed record TaskAttachment(string Id, string FileName, long Size, DateTimeOffset Added, string AddedBy)
{
    public bool IsPicture => Webignite.Core.Files.FileKinds.IsPicture(FileName);
}

/// <summary>A file in a project's Files tab: the task it's on, and the file.</summary>
public sealed record ProjectFile(ProjectTask Task, TaskAttachment Attachment);

/// <summary>Attachments on tasks, and the picture on a card, as Asana's.</summary>
public static class Attachments
{
    /// <summary>A file attached: added last; the first picture becomes the card's cover when it has none.</summary>
    public static ProjectTask Add(ProjectTask task, TaskAttachment attachment) => task with
    {
        Attachments = [.. task.Attachments, attachment],
        CoverId = task.CoverId ?? (attachment.IsPicture ? attachment.Id : null),
    };

    /// <summary>A file taken off; when it was the cover, the card has none.</summary>
    public static ProjectTask Remove(ProjectTask task, string attachmentId) => task with
    {
        Attachments = [.. task.Attachments.Where(a => a.Id != attachmentId)],
        CoverId = task.CoverId == attachmentId ? null : task.CoverId,
    };

    /// <summary>A picture as the card's cover, or none.</summary>
    public static ProjectTask SetCover(ProjectTask task, string? attachmentId) =>
        task with { CoverId = attachmentId is not null && task.Attachments.Any(a => a.Id == attachmentId && a.IsPicture) ? attachmentId : null };

    /// <summary>The card's cover picture, if it has one still attached.</summary>
    public static TaskAttachment? Cover(ProjectTask task) =>
        task.CoverId is { } id ? task.Attachments.FirstOrDefault(a => a.Id == id && a.IsPicture) : null;

    /// <summary>Every file on a project's tasks and subtasks, the newest first, for its Files tab.</summary>
    public static IReadOnlyList<ProjectFile> Of(IEnumerable<ProjectTask> tasks) =>
        [.. tasks.Where(t => t.Deleted is null).SelectMany(t => t.Attachments.Select(a => new ProjectFile(t, a))).OrderByDescending(f => f.Attachment.Added)];

    /// <summary>A file's name made safe to keep on disk: no folder signs or characters Windows refuses.</summary>
    public static string SafeName(string name)
    {
        var file = System.IO.Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        var bad = System.IO.Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*']).ToHashSet();
        var safe = new string([.. file.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim().TrimEnd('.');
        return safe.Length == 0 ? "file" : safe;
    }
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

    /// <summary>Its description on the Overview tab, as plain text; <see cref="NotesHtml"/> keeps any formatting.</summary>
    public string Notes { get; init; } = "";
    public string NotesHtml { get; init; } = "";

    /// <summary>Key resources on the Overview tab: links to the brief, the staging site and so on.</summary>
    public IReadOnlyList<ProjectLink> Links { get; init; } = [];

    /// <summary>Status updates, the newest first.</summary>
    public IReadOnlyList<StatusUpdate> Updates { get; init; } = [];

    /// <summary>The fields it adds to its tasks, as Asana's custom fields, in the order they show.</summary>
    public IReadOnlyList<CustomField> CustomFields { get; init; } = [];

    /// <summary>Its picture in the sidebar and on Home: an icon's name from <see cref="ProjectLooks.Icons"/>.</summary>
    public string Icon { get; init; } = "";

    /// <summary>Its colour from <see cref="ProjectLooks.Colours"/>; empty picks one from its name.</summary>
    public string Colour { get; init; } = "";

    public ProjectHealth Health { get; init; }

    /// <summary>Starred: shown first in the sidebar's list of projects.</summary>
    public bool Favourite { get; init; }

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

    /// <summary>The description as plain text, for search, templates and assistants.</summary>
    public string Notes { get; init; } = "";

    /// <summary>The description with its formatting (bold, lists, links), as the editor's HTML; empty for plain notes.</summary>
    public string NotesHtml { get; init; } = "";

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

    /// <summary>Files attached to it, kept on this PC.</summary>
    public IReadOnlyList<TaskAttachment> Attachments { get; init; } = [];

    /// <summary>The picture shown on top of its card on the board, as Asana's cover; null for none.</summary>
    public string? CoverId { get; init; }

    /// <summary>A milestone, as Asana's: a moment that matters, shown with a diamond and on the Overview tab.</summary>
    public bool Milestone { get; init; }

    /// <summary>The tasks in its project it's waiting on, by id, as Asana's dependencies.</summary>
    public IReadOnlyList<string> BlockedBy { get; init; } = [];

    /// <summary>Its values for the project's custom fields, by field id (see <see cref="CustomFields"/>).</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The repeating task this is the next one of, by id; null for a task made any other way. While this one is
    /// there, ticking that one off again doesn't make another.
    /// </summary>
    public string? RepeatOf { get; init; }

    /// <summary>
    /// The day a monthly or yearly repeat counts from, so one due on the 31st comes back on the 31st after a short
    /// month (31 Jan, 28 Feb, 31 Mar). Null for the first one, which counts from its own due date.
    /// </summary>
    public DateOnly? RepeatFrom { get; init; }

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

    /// <summary>The place before the first one, for something added at the top, as a board column's + does.</summary>
    public static double FirstOrder(IEnumerable<double> orders) => orders.DefaultIfEmpty(2).Min() - 1;

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

    /// <summary>
    /// Where a heading dragged among a project's headings lands: just before <paramref name="beforeId"/>, or last
    /// when that's null. Returns its new order, or null when it wouldn't move. Headings are shared by the list and
    /// the board, so one order serves both, as in Asana.
    /// </summary>
    public static double? PlaceHeading(IReadOnlyList<Heading> headings, string movingId, string? beforeId)
    {
        var list = headings.Where(h => h.Deleted is null).OrderBy(h => h.Order).ToList();
        var from = list.FindIndex(h => h.Id == movingId);
        if (from < 0 || movingId == beforeId)
            return null;
        var others = list.Where(h => h.Id != movingId).ToList();
        var index = beforeId is null ? others.Count : others.FindIndex(h => h.Id == beforeId);
        if (index < 0 || index == from)
            return null;
        return Between(index > 0 ? others[index - 1].Order : null, index < others.Count ? others[index].Order : null);
    }

    /// <summary>
    /// A task moved off a deleted heading, put back on it when the delete is undone: only while it's still a task
    /// under no heading in the heading's project. Otherwise (moved under another heading or to another project, or
    /// made a subtask since) the same task, as it is. Its subtasks follow it.
    /// </summary>
    public static ProjectTask BackOnHeading(ProjectTask task, Heading heading) =>
        task.Deleted is null && task.ParentId is null && task.HeadingId is null && task.ProjectId == heading.ProjectId
            ? task with { HeadingId = heading.Id }
            : task;

    /// <summary>A task's description to edit: its formatted notes, or its plain notes when it has none.</summary>
    public static Webignite.Core.Writing.RichDocument Description(ProjectTask task) => RichNotes.Read(task.Notes, task.NotesHtml);

    /// <summary>A description written: the plain text and, when there's any formatting, the HTML. Nothing written clears both.</summary>
    public static ProjectTask WithDescription(ProjectTask task, Webignite.Core.Writing.RichDocument description)
    {
        var (notes, html) = RichNotes.Write(description);
        return task with { Notes = notes, NotesHtml = html };
    }

    /// <summary>"0/1" for the subtasks header, as Asana's; empty when there are none.</summary>
    public static string SubtaskTally(IEnumerable<ProjectTask> subtasks)
    {
        var list = subtasks.Where(t => t.Deleted is null).ToList();
        return list.Count == 0 ? "" : $"{list.Count(t => t.Done)}/{list.Count}";
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
    /// Tasks by when they're due: overdue, today, the rest of this week, later, and no date, then those already
    /// done. Within a group, by date, then High before Normal before Low. The week starts on
    /// <paramref name="firstDay"/>; screens pass the PC's.
    /// </summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<ProjectTask> Tasks)> ByWhen(IEnumerable<ProjectTask> tasks, DateOnly today,
        DayOfWeek firstDay = DayOfWeek.Monday)
    {
        var weekEnd = Dates.WeekStart(today, firstDay).AddDays(6);
        static int Rank(TaskPriority p) => p switch { TaskPriority.High => 0, TaskPriority.Normal => 1, _ => 2 };
        var sorted = tasks.OrderBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => Rank(t.Priority)).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        var open = sorted.Where(t => !t.Done).ToList();
        (string, IReadOnlyList<ProjectTask>)[] groups =
        [
            ("Overdue", open.Where(t => t.Due < today).ToList()),
            ("Today", open.Where(t => t.Due == today).ToList()),
            ("This week", open.Where(t => t.Due > today && t.Due <= weekEnd).ToList()),
            ("Later", open.Where(t => t.Due > weekEnd).ToList()),
            ("No due date", open.Where(t => t.Due is null).ToList()),
            ("Done", sorted.Where(t => t.Done).ToList()),
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
    /// when it had no date), its start moved by as much, under the same heading, straight after it. It notes which
    /// task it repeats, and has no files: those stay with the one ticked off. Null when it doesn't repeat.
    /// </summary>
    public static ProjectTask? Next(ProjectTask done, DateOnly today, double order) =>
        NextDue(done, today) is { } next ? Copy(done, next.Due, next.Anchor, today, order) : null;

    /// <summary>
    /// The next one of a repeating task with its subtasks, the task first: each subtask fresh and not done, its
    /// dates moved by the same step as the task's (from today when the task had no date), with no files and
    /// waiting on nothing. Empty when it doesn't repeat.
    /// </summary>
    public static IReadOnlyList<ProjectTask> Next(ProjectTask done, IEnumerable<ProjectTask> subtasks, DateOnly today, double order)
    {
        if (NextDue(done, today) is not { } due)
            return [];
        var next = Copy(done, due.Due, due.Anchor, today, order);
        var shift = due.Due.DayNumber - (done.Due ?? today).DayNumber;
        return [next, .. subtasks.Where(t => t.Deleted is null && t.ParentId == done.Id).OrderBy(t => t.Order).Select(t => t with
        {
            Id = NewId(),
            ParentId = next.Id,
            HeadingId = next.HeadingId,
            Done = false,
            Start = t.Start?.AddDays(shift),
            Due = t.Due?.AddDays(shift),
            Attachments = [],
            CoverId = null,
            BlockedBy = [],
            Created = default,
            Changed = default,
            Deleted = null,
            Fields = new Dictionary<string, Stamp>(),
        })];
    }

    /// <summary>Whether a repeating task already has its next one, not deleted, so ticking it again makes no other.</summary>
    public static bool HasNext(ProjectTask done, IEnumerable<ProjectTask> tasks) =>
        tasks.Any(t => t.Deleted is null && t.RepeatOf == done.Id);

    private static ProjectTask Copy(ProjectTask done, DateOnly due, DateOnly? anchor, DateOnly today, double order)
    {
        var shift = due.DayNumber - (done.Due ?? today).DayNumber;
        return done with
        {
            Id = NewId(),
            RepeatOf = done.Id,
            RepeatFrom = anchor,
            Done = false,
            Due = due,
            Start = done.Start?.AddDays(shift),
            Attachments = [],
            CoverId = null,
            Order = order,
            Created = default,
            Changed = default,
            Deleted = null,
            Fields = new Dictionary<string, Stamp>(),
        };
    }

    // When the next one is due, and the day a monthly or yearly repeat counts from. The day kept on the task is
    // used only while its due date still lands on a step from it; a due date moved by hand starts afresh.
    private static (DateOnly Due, DateOnly? Anchor)? NextDue(ProjectTask done, DateOnly today)
    {
        if (done.Repeat == TaskRepeat.None)
            return null;
        var from = done.Due ?? today;
        var anchor = done.RepeatFrom is { } kept && Steps(kept, from, done.Repeat) is not null ? kept : from;
        var due = StepAfter(anchor, from, done.Repeat);
        // Ticked off late: skip ahead so the next one isn't already overdue.
        while (due < today)
            due = StepAfter(anchor, due, done.Repeat);
        return (due, done.Repeat is TaskRepeat.Monthly or TaskRepeat.Yearly ? anchor : null);
    }

    // The first step from the anchor that's after a day: a day or a week on, or the anchor's day in a later month
    // or year (the month's last day when it's shorter).
    private static DateOnly StepAfter(DateOnly anchor, DateOnly after, TaskRepeat repeat)
    {
        switch (repeat)
        {
            case TaskRepeat.Daily:
                return after.AddDays(1);
            case TaskRepeat.Weekly:
                return after.AddDays(7);
            default:
                var count = Steps(anchor, after, repeat) ?? Math.Max(-1, Distance(anchor, after, repeat) - 1);
                DateOnly next;
                do
                    next = Step(anchor, ++count, repeat);
                while (next <= after);
                return next;
        }
    }

    // How many whole months or years a day is from the anchor, when it lands exactly on one of its steps.
    private static int? Steps(DateOnly anchor, DateOnly day, TaskRepeat repeat)
    {
        var count = Distance(anchor, day, repeat);
        return count >= 0 && Step(anchor, count, repeat) == day ? count : null;
    }

    private static int Distance(DateOnly anchor, DateOnly day, TaskRepeat repeat) =>
        repeat == TaskRepeat.Yearly ? day.Year - anchor.Year : (day.Year - anchor.Year) * 12 + day.Month - anchor.Month;

    private static DateOnly Step(DateOnly anchor, int count, TaskRepeat repeat) =>
        repeat == TaskRepeat.Yearly ? anchor.AddYears(count) : anchor.AddMonths(count);

    /// <summary>
    /// A project as a template: its headings in order, its tasks with their subtasks' titles, and dates as days
    /// after the earliest date in it. Who tasks were given to, what's done and links to mail or invoices stay behind,
    /// and so do repeating tasks already ticked off (the next one is there). Tasks keep their heading by its place,
    /// and what waits on what by place too, subtasks included.
    /// </summary>
    public static ProjectTemplate TemplateOf(string name, IEnumerable<Heading> headings, IEnumerable<ProjectTask> tasks, IReadOnlyList<CustomField>? fields = null)
    {
        var live = headings.Where(h => h.Deleted is null).OrderBy(h => h.Order).ToList();
        // A repeating task already ticked off has a next one carrying on, so only that one goes in.
        var all = tasks.Where(t => t.Deleted is null && !(t.Done && t.Repeat != TaskRepeat.None)).ToList();
        var top = Sections(live, all).SelectMany(s => s.Tasks).ToList();
        IReadOnlyList<ProjectTask> SubtasksOf(ProjectTask t) => [.. all.Where(s => s.ParentId == t.Id).OrderBy(s => s.Order)];
        var dates = top.SelectMany(t => new[] { t.Start, t.Due }).OfType<DateOnly>().ToList();
        var first = dates.Count == 0 ? (DateOnly?)null : dates.Min();
        int? Day(DateOnly? date) => date is { } d && first is { } f ? d.DayNumber - f.DayNumber : null;
        int? HeadingAt(string? id) => live.FindIndex(h => h.Id == id) is var at and >= 0 ? at : null;
        // Dependencies and field values come along by place, so a project made from it waits and reads the same.
        var place = top.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var flat = top.SelectMany(t => SubtasksOf(t).Prepend(t)).ToList();
        var spot = flat.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var known = (fields ?? []).Select(f => f.Id).ToHashSet();
        return new ProjectTemplate
        {
            Id = NewId(),
            Name = name.Trim(),
            Headings = [.. live.Select(h => h.Name)],
            Fields = fields ?? [],
            Tasks = [.. top.Select(t => new TemplateTask
            {
                Title = t.Title,
                Heading = live.FirstOrDefault(h => h.Id == t.HeadingId)?.Name,
                HeadingIndex = HeadingAt(t.HeadingId),
                Notes = t.Notes,
                Priority = t.Priority,
                Tags = t.Tags,
                Repeat = t.Repeat,
                StartDay = Day(t.Start),
                DueDay = Day(t.Due),
                Subtasks = [.. SubtasksOf(t).Select(s => s.Title)],
                Milestone = t.Milestone,
                Values = t.Values.Where(v => known.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value),
                WaitsOn = [.. t.BlockedBy.Where(place.ContainsKey).Select(id => place[id])],
            })],
            Waits = [.. flat.SelectMany((t, i) => t.BlockedBy.Where(spot.ContainsKey).Select(id => new TemplateWait(i, spot[id])))],
        };
    }

    /// <summary>
    /// A new project from a template, starting on a day: its headings, tasks and subtasks, all new, waiting on each
    /// other as in the template.
    /// </summary>
    public static (Project Project, IReadOnlyList<Heading> Headings, IReadOnlyList<ProjectTask> Tasks) FromTemplate(
        ProjectTemplate template, string name, DateOnly start, double order)
    {
        // The template's fields come over with new ids, and the values of each task with them.
        var fieldIds = template.Fields.ToDictionary(f => f.Id, _ => NewId());
        var optionIds = template.Fields.SelectMany(f => f.Options).ToDictionary(o => o.Id, _ => NewId());
        var fields = template.Fields.Select(f => f with
        {
            Id = fieldIds[f.Id],
            Options = [.. f.Options.Select(o => o with { Id = optionIds[o.Id] })],
        }).ToList();
        string Value(string value) => optionIds.TryGetValue(value, out var option) ? option : value;
        var project = new Project
        {
            Id = NewId(), Name = string.IsNullOrWhiteSpace(name) ? template.Name : name.Trim(), Order = order, CustomFields = fields,
        };
        var headings = template.Headings.Select((h, i) => new Heading { Id = NewId(), ProjectId = project.Id, Name = h, Order = i + 1 }).ToList();
        // By place when the template has it; templates kept before that find their heading by name.
        string? HeadingOf(TemplateTask t) => t.HeadingIndex is { } at && at >= 0 && at < headings.Count
            ? headings[at].Id
            : headings.FirstOrDefault(h => h.Name == t.Heading)?.Id;
        // Each task followed by its subtasks, the order the template counts them in for what waits on what.
        var tasks = new List<ProjectTask>();
        var made = new List<int>();
        var place = 0;
        foreach (var t in template.Tasks)
        {
            var task = new ProjectTask
            {
                Id = NewId(),
                ProjectId = project.Id,
                HeadingId = HeadingOf(t),
                Title = t.Title,
                Notes = t.Notes,
                Priority = t.Priority,
                Tags = t.Tags,
                Repeat = t.Repeat,
                Start = t.StartDay is { } s ? start.AddDays(s) : null,
                Due = t.DueDay is { } d ? start.AddDays(d) : null,
                Order = ++place,
                Milestone = t.Milestone,
                Values = t.Values.Where(v => fieldIds.ContainsKey(v.Key)).ToDictionary(v => fieldIds[v.Key], v => Value(v.Value)),
            };
            made.Add(tasks.Count);
            tasks.Add(task);
            tasks.AddRange(t.Subtasks.Select((title, i) => new ProjectTask
            {
                Id = NewId(), ProjectId = project.Id, HeadingId = task.HeadingId, ParentId = task.Id, Title = title, Order = i + 1,
            }));
        }
        // What each waits on, now that every task has its new id: by place among tasks and subtasks, or, for a
        // template kept before subtasks were counted, by place among the tasks alone.
        var waits = template.Waits.Count > 0
            ? template.Waits.Where(w => w.Task >= 0 && w.Task < tasks.Count && w.On >= 0 && w.On < tasks.Count && w.Task != w.On)
                .Select(w => (Task: w.Task, On: w.On))
            : template.Tasks.SelectMany((t, i) => t.WaitsOn.Where(w => w >= 0 && w < made.Count && w != i).Select(w => (Task: made[i], On: made[w])));
        foreach (var group in waits.GroupBy(w => w.Task))
            tasks[group.Key] = tasks[group.Key] with { BlockedBy = [.. group.Select(w => tasks[w.On].Id).Distinct()] };
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
        ("icon", p => p.Icon), ("colour", p => p.Colour), ("health", p => p.Health), ("favourite", p => p.Favourite),
        ("name", p => p.Name), ("shared", p => p.Shared), ("client", p => p.ClientId), ("status", p => p.Status),
        ("due", p => p.Due), ("notes", p => p.Notes), ("order", p => p.Order),
        ("noteshtml", p => p.NotesHtml), ("links", p => p.Links), ("updates", p => p.Updates), ("customfields", p => p.CustomFields),
    ];

    private static readonly (string Name, Func<Heading, object?> Value)[] HeadingFields =
    [
        ("name", h => h.Name), ("order", h => h.Order),
    ];

    private static readonly (string Name, Func<ProjectTask, object?> Value)[] TaskFields =
    [
        ("project", t => t.ProjectId), ("heading", t => t.HeadingId), ("parent", t => t.ParentId), ("title", t => t.Title), ("notes", t => t.Notes), ("noteshtml", t => t.NotesHtml),
        ("assigned", t => t.AssignedTo), ("start", t => t.Start), ("due", t => t.Due), ("done", t => t.Done), ("order", t => t.Order),
        ("client", t => t.ClientId), ("document", t => t.DocumentId), ("mail", t => t.Mail), ("priority", t => t.Priority), ("tags", t => t.Tags), ("repeat", t => t.Repeat),
        ("attachments", t => t.Attachments), ("cover", t => t.CoverId), ("milestone", t => t.Milestone), ("blockedby", t => t.BlockedBy), ("values", t => t.Values),
        ("repeatof", t => t.RepeatOf), ("repeatfrom", t => t.RepeatFrom),
    ];

    /// <summary>The project as saved: who made it, and who changed which field, now.</summary>
    public static Project Stamped(Project? before, Project after, Stamp now)
    {
        var (created, fields, changed) = Mark(before, after, now, ProjectFields);
        return after with { Created = created, Fields = fields, Changed = changed ? now : before!.Changed, Deleted = before?.Deleted };
    }

    private static readonly (string Name, Func<TaskComment, object?> Value)[] CommentFields = [("text", c => c.Text)];

    public static TaskComment Stamped(TaskComment? before, TaskComment after, Stamp now)
    {
        var (created, fields, changed) = Mark(before, after, now, CommentFields);
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

/// <summary>
/// The icons and colours a project can have, as in Asana: a coloured square with a small picture. A project with
/// none picked gets a colour from its name, so new projects differ at a glance.
/// </summary>
public static class ProjectLooks
{
    /// <summary>
    /// Icon names, Asana's 32 project icons; each is drawn as "Project.{name}" in the app's icon set. Asana's
    /// default is the list.
    /// </summary>
    public static IReadOnlyList<string> Icons { get; } =
    [
        "List", "Board", "Timeline", "Calendar", "Rocket", "People", "Graph", "Star",
        "Bug", "LightBulb", "Globe", "Gear", "Notebook", "Computer", "Check", "Target",
        "Html", "Megaphone", "ChatBubbles", "Briefcase", "PageLayout", "MountainFlag", "Puzzle", "Presentation",
        "LineAndSymbols", "SpeedDial", "Ribbon", "Shoe", "ShoppingBasket", "Map", "Ticket", "Coins",
    ];

    /// <summary>
    /// Colour names, Asana's 16 in its order; each has a "Project{name}" brush in the palette. Red and green are
    /// allowed here, on a project's square only; everywhere else they still mean problem and done.
    /// </summary>
    public static IReadOnlyList<string> Colours { get; } =
    [
        "Red", "Orange", "YellowOrange", "Yellow", "YellowGreen", "Green", "BlueGreen", "Aqua",
        "Blue", "Indigo", "Purple", "Magenta", "HotPink", "Pink", "CoolGrey", "None",
    ];

    // Names used before Asana's set came in, so projects keep a look close to the one picked.
    private static readonly Dictionary<string, string> OldIcons = new(StringComparer.Ordinal)
    {
        ["Projects"] = "List", ["Checklist"] = "Check", ["Tiles"] = "Board", ["Flag"] = "MountainFlag",
        ["Mail"] = "ChatBubbles", ["Folder"] = "Briefcase", ["Picture"] = "PageLayout", ["Clients"] = "People",
        ["Money"] = "Coins", ["Domains"] = "Globe", ["SiteCheck"] = "Graph", ["Markup"] = "LineAndSymbols",
        ["Notes"] = "Notebook", ["Bell"] = "Megaphone",
    };

    private static readonly Dictionary<string, string> OldColours = new(StringComparer.Ordinal)
    {
        ["Teal"] = "BlueGreen", ["Grey"] = "CoolGrey",
    };

    // The colours a name can land on by itself: bright ones, never grey or none.
    private static readonly string[] Picked = [.. Colours.Where(c => c is not ("CoolGrey" or "None"))];

    public static string IconOf(Project project) =>
        Icons.Contains(project.Icon) ? project.Icon : OldIcons.GetValueOrDefault(project.Icon) ?? Icons[0];

    public static string ColourOf(Project project) =>
        Colours.Contains(project.Colour) ? project.Colour : OldColours.GetValueOrDefault(project.Colour) ?? ColourFor(project.Name);

    /// <summary>A colour from a name, always the same for the same name.</summary>
    public static string ColourFor(string name)
    {
        var sum = 0;
        foreach (var c in name.Trim().ToUpperInvariant())
            sum = (sum * 31 + c) % 9973;
        return Picked[sum % Picked.Length];
    }

    /// <summary>A colour's name as Asana shows it, in plain words.</summary>
    public static string ColourLabel(string colour) => colour switch
    {
        "YellowOrange" => "Yellow-orange",
        "YellowGreen" => "Yellow-green",
        "BlueGreen" => "Blue-green",
        "HotPink" => "Hot pink",
        "CoolGrey" => "Cool grey",
        "None" => "No colour",
        _ => colour,
    };

    /// <summary>An icon's name in plain words, for its tip.</summary>
    public static string IconLabel(string icon) => icon switch
    {
        "LightBulb" => "Light bulb",
        "Html" => "Code",
        "ChatBubbles" => "Chat bubbles",
        "PageLayout" => "Page layout",
        "MountainFlag" => "Mountain",
        "LineAndSymbols" => "Shapes",
        "SpeedDial" => "Speed dial",
        "ShoppingBasket" => "Shopping basket",
        _ => icon,
    };

    public static string Label(ProjectHealth health) => health switch
    {
        ProjectHealth.OnTrack => "On track",
        ProjectHealth.AtRisk => "At risk",
        ProjectHealth.OffTrack => "Off track",
        _ => "Set status",
    };

    /// <summary>
    /// Where a project dragged in a list of projects shown starred first lands: just above or below
    /// <paramref name="targetId"/>. It takes the target's star, so dropping it among the starred ones stars it,
    /// and its order goes between its new neighbours that share that star. Null when nothing would change.
    /// </summary>
    public static (double Order, bool Favourite)? PlaceAt(IReadOnlyList<Project> shown, string movingId, string targetId, bool below)
    {
        var moving = shown.FirstOrDefault(p => p.Id == movingId);
        var others = shown.Where(p => p.Id != movingId).ToList();
        var target = others.FindIndex(p => p.Id == targetId);
        if (moving is null || target < 0)
            return null;
        var favourite = others[target].Favourite;
        var index = target + (below ? 1 : 0);
        var before = index > 0 && others[index - 1].Favourite == favourite ? others[index - 1].Order : (double?)null;
        var after = index < others.Count && others[index].Favourite == favourite ? others[index].Order : (double?)null;
        var order = ProjectRules.Between(before, after);
        var same = shown.ToList().IndexOf(moving) == index && moving.Favourite == favourite;
        return same ? null : (order, favourite);
    }

    /// <summary>The sidebar's projects: starred first, then in their usual order.</summary>
    public static IReadOnlyList<Project> SidebarOrder(IEnumerable<Project> projects) =>
        [.. projects.OrderByDescending(p => p.Favourite).ThenBy(p => p.Order)];
}
