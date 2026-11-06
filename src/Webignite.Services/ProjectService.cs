using Webignite.Core.Projects;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Projects, headings, tasks and subtasks, for the Projects page and later My tasks, Planner and Calendar. Every
/// change is made to the record as stored now and stamped with who made it and when, field by field, so sync
/// can be added later without redoing anything. Deleting leaves a marker; Undo takes it away again.
/// <see cref="Changed"/> tells every open page, with the project's id, or null when it's about projects as a whole.
/// </summary>
public sealed class ProjectService(LocalStore store, Func<string> me, TimeProvider? clock = null, string? attachmentsFolder = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    /// <summary>Where attached files are kept on this PC: a folder per file, under its task's folder.</summary>
    public string AttachmentsFolder { get; } = attachmentsFolder ?? Path.Combine(DataPaths.Root(), "attachments");

    /// <summary>Where an attached file is kept.</summary>
    public string PathOf(string taskId, TaskAttachment attachment) =>
        Path.Combine(AttachmentsFolder, taskId, attachment.Id, Attachments.SafeName(attachment.FileName));

    /// <summary>
    /// Attaches a file to a task: a copy is kept with the app, so the task still has it when the original moves.
    /// The first picture becomes the card's cover. Null with the problem when the file can't be read.
    /// </summary>
    public (ProjectTask? Saved, string? Problem) Attach(string taskId, string sourcePath)
    {
        if (GetTask(taskId) is null)
            return (null, "The task is gone.");
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
            return (null, $"{Path.GetFileName(sourcePath)} isn't there any more.");
        var attachment = new TaskAttachment(ProjectRules.NewId(), Attachments.SafeName(info.Name), info.Length, clock.GetUtcNow(), me());
        var target = PathOf(taskId, attachment);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourcePath, target, overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Attaching a file to a task", e);
            return (null, $"{info.Name} couldn't be copied: {e.Message}");
        }
        var (saved, problems) = ChangeTask(taskId, t => Attachments.Add(t, attachment));
        if (saved is null)
            TryDelete(Path.GetDirectoryName(target)!);
        return (saved, saved is null ? string.Join(" ", problems) : null);
    }

    /// <summary>Takes a file off a task and deletes the copy kept with the app.</summary>
    public ProjectTask? Detach(string taskId, string attachmentId)
    {
        if (GetTask(taskId) is not { } task || task.Attachments.FirstOrDefault(a => a.Id == attachmentId) is not { } attachment)
            return null;
        var (saved, _) = ChangeTask(taskId, t => Attachments.Remove(t, attachmentId));
        if (saved is not null)
            TryDelete(Path.GetDirectoryName(PathOf(taskId, attachment))!);
        return saved;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Deleting an attached file", e);
        }
    }

    public event Action<string?>? Changed;

    private Stamp Now() => new(me(), clock.GetUtcNow());

    // Projects

    /// <summary>The projects not deleted, in their order.</summary>
    public IReadOnlyList<Project> Projects() =>
        [.. store.ListProjects().Where(p => p.Deleted is null).OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];

    public Project? Get(string id) => store.GetProject(id) is { Deleted: null } project ? project : null;

    /// <summary>A new project at the end of the list. Returns the problems instead, saving nothing.</summary>
    public (Project? Saved, IReadOnlyList<string> Problems) Add(Project typed)
    {
        var project = typed with { Name = typed.Name.Trim(), Order = ProjectRules.NextOrder(store.ListProjects().Select(p => p.Order)) };
        var problems = ProjectRules.Check(project);
        if (problems.Count > 0)
            return (null, problems);
        var saved = store.SaveProject(ProjectRules.Stamped(null, project, Now()));
        Changed?.Invoke(null);
        return (saved, []);
    }

    /// <summary>Changes a project as it's stored now. Null when it's gone; the problems when the change isn't allowed.</summary>
    public (Project? Saved, IReadOnlyList<string> Problems) Change(string id, Func<Project, Project> change)
    {
        if (Get(id) is not { } current)
            return (null, ["The project is gone."]);
        var changed = change(current);
        changed = changed with { Id = current.Id, Name = changed.Name.Trim() };
        var problems = ProjectRules.Check(changed);
        if (problems.Count > 0)
            return (null, problems);
        var saved = store.SaveProject(ProjectRules.Stamped(current, changed, Now()));
        Changed?.Invoke(id);
        return (saved, []);
    }

    /// <summary>Deletes a project with its headings and tasks. Returns what went, for Undo.</summary>
    public ProjectWork? Delete(string id)
    {
        if (Get(id) is not { } project)
            return null;
        var now = Now();
        var headings = Headings(id);
        var tasks = AllTasks(id);
        store.SaveProjectWork([Remove(project, now)], headings.Select(h => Remove(h, now)), tasks.Select(t => Remove(t, now)));
        Changed?.Invoke(null);
        return new ProjectWork([project], headings, tasks);
    }

    /// <summary>Open tasks and subtasks given to this person, in projects that aren't done (see <see cref="ProjectRules.Mine"/>).</summary>
    public IReadOnlyList<ProjectTask> Mine(string meId) => ProjectRules.Mine(store.ListProjectTasks(), store.ListProjects(), meId);

    /// <summary>This person's tasks due in a range, for Calendar, Planner and Today.</summary>
    public IReadOnlyList<Webignite.Core.Calendar.AgendaItem> Agenda(DateOnly from, DateOnly to, string meId) =>
        ProjectRules.Agenda(from, to, Mine(meId), store.ListProjects().ToDictionary(p => p.Id, p => p.Name));

    // Headings

    /// <summary>A project's headings, in their order.</summary>
    public IReadOnlyList<Heading> Headings(string projectId) =>
        [.. store.ListHeadings().Where(h => h.ProjectId == projectId && h.Deleted is null).OrderBy(h => h.Order)];

    /// <summary>A new heading at the end of a project.</summary>
    public (Heading? Saved, IReadOnlyList<string> Problems) AddHeading(string projectId, string name)
    {
        if (Get(projectId) is null)
            return (null, ["The project is gone."]);
        var heading = new Heading
        {
            Id = ProjectRules.NewId(), ProjectId = projectId, Name = name.Trim(),
            Order = ProjectRules.NextOrder(Headings(projectId).Select(h => h.Order)),
        };
        var problems = ProjectRules.Check(heading);
        if (problems.Count > 0)
            return (null, problems);
        var saved = store.SaveHeading(ProjectRules.Stamped(null, heading, Now()));
        Changed?.Invoke(projectId);
        return (saved, []);
    }

    public (Heading? Saved, IReadOnlyList<string> Problems) ChangeHeading(string id, Func<Heading, Heading> change)
    {
        if (store.GetHeading(id) is not { Deleted: null } current)
            return (null, ["The heading is gone."]);
        var changed = change(current);
        changed = changed with { Id = current.Id, ProjectId = current.ProjectId, Name = changed.Name.Trim() };
        var problems = ProjectRules.Check(changed);
        if (problems.Count > 0)
            return (null, problems);
        var saved = store.SaveHeading(ProjectRules.Stamped(current, changed, Now()));
        Changed?.Invoke(current.ProjectId);
        return (saved, []);
    }

    /// <summary>Deletes a heading; its tasks stay in the project, under no heading. Returns what changed, for Undo.</summary>
    public ProjectWork? DeleteHeading(string id)
    {
        if (store.GetHeading(id) is not { Deleted: null } heading)
            return null;
        var now = Now();
        var moved = AllTasks(heading.ProjectId).Where(t => t.HeadingId == id).ToList();
        store.SaveProjectWork([], [Remove(heading, now)], moved.Select(t => ProjectRules.Stamped(t, t with { HeadingId = null }, now)));
        Changed?.Invoke(heading.ProjectId);
        return new ProjectWork([], [heading], moved);
    }

    // Tasks

    /// <summary>A project's tasks and subtasks, not deleted, in their order.</summary>
    public IReadOnlyList<ProjectTask> AllTasks(string projectId) =>
        [.. store.ListProjectTasks().Where(t => t.ProjectId == projectId && t.Deleted is null).OrderBy(t => t.Order)];

    public ProjectTask? GetTask(string id) => store.GetProjectTask(id) is { Deleted: null } task ? task : null;

    /// <summary>A new task (or subtask) at the end of its heading, or its task's subtasks.</summary>
    public (ProjectTask? Saved, IReadOnlyList<string> Problems) AddTask(ProjectTask typed)
    {
        if (Get(typed.ProjectId) is null)
            return (null, ["The project is gone."]);
        var siblings = AllTasks(typed.ProjectId).Where(t => t.ParentId == typed.ParentId && (typed.ParentId is not null || t.HeadingId == typed.HeadingId));
        var task = typed with { Title = typed.Title.Trim(), Order = ProjectRules.NextOrder(siblings.Select(t => t.Order)) };
        var problems = ProjectRules.Check(task, task.ParentId is { } parent ? GetTask(parent) : null);
        if (problems.Count > 0)
            return (null, problems);
        var saved = store.SaveProjectTask(ProjectRules.Stamped(null, task, Now()));
        Changed?.Invoke(task.ProjectId);
        return (saved, []);
    }

    /// <summary>Changes a task as it's stored now. A task moved to another heading takes its subtasks along.</summary>
    public (ProjectTask? Saved, IReadOnlyList<string> Problems) ChangeTask(string id, Func<ProjectTask, ProjectTask> change)
    {
        if (GetTask(id) is not { } current)
            return (null, ["The task is gone."]);
        var changed = change(current);
        changed = changed with { Id = current.Id, ProjectId = current.ProjectId, Title = changed.Title.Trim() };
        var problems = ProjectRules.Check(changed, changed.ParentId is { } parent ? GetTask(parent) : null);
        if (changed.ParentId is not null && AllTasks(current.ProjectId).Any(t => t.ParentId == id))
            problems = [.. problems, "A task with subtasks can't become a subtask."];
        if (problems.Count > 0)
            return (null, problems);
        var now = Now();
        var subtasks = changed.HeadingId != current.HeadingId
            ? AllTasks(current.ProjectId).Where(t => t.ParentId == id).Select(t => ProjectRules.Stamped(t, t with { HeadingId = changed.HeadingId }, now)).ToList()
            : [];
        var saved = ProjectRules.Stamped(current, changed, now);
        store.SaveProjectWork([], [], [saved, .. subtasks, .. NextOf(current, saved, now)]);
        Changed?.Invoke(current.ProjectId);
        return (saved, []);
    }

    /// <summary>
    /// Moves a task to another project, as when it's dropped on the project in the sidebar: it goes last among the
    /// tasks under no heading there, and takes its subtasks along. A subtask moved on its own becomes a task.
    /// </summary>
    public (ProjectTask? Saved, IReadOnlyList<string> Problems) MoveTask(string id, string projectId)
    {
        if (GetTask(id) is not { } current)
            return (null, ["The task is gone."]);
        if (Get(projectId) is null)
            return (null, ["The project is gone."]);
        if (current.ProjectId == projectId)
            return (current, []);
        var now = Now();
        var loose = AllTasks(projectId).Where(t => t.ParentId is null && t.HeadingId is null);
        var moved = ProjectRules.Stamped(current, current with { ProjectId = projectId, HeadingId = null, ParentId = null, Order = ProjectRules.NextOrder(loose.Select(t => t.Order)) }, now);
        var subtasks = current.ParentId is null
            ? AllTasks(current.ProjectId).Where(t => t.ParentId == id).Select(t => ProjectRules.Stamped(t, t with { ProjectId = projectId, HeadingId = null }, now)).ToList()
            : [];
        store.SaveProjectWork([], [], [moved, .. subtasks]);
        Changed?.Invoke(current.ProjectId);
        Changed?.Invoke(projectId);
        return (moved, []);
    }

    // A repeating task just ticked off: the next one, straight after it, with its subtasks fresh.
    private List<ProjectTask> NextOf(ProjectTask before, ProjectTask after, Stamp now)
    {
        if (before.Done || !after.Done || after.ParentId is not null)
            return [];
        var siblings = AllTasks(after.ProjectId).Where(t => t.ParentId is null && t.HeadingId == after.HeadingId && t.Order > after.Order);
        var nextOrder = siblings.Select(t => (double?)t.Order).Min();
        if (ProjectRules.Next(after, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ProjectRules.Between(after.Order, nextOrder)) is not { } next)
            return [];
        var made = new List<ProjectTask> { ProjectRules.Stamped(null, next, now) };
        made.AddRange(AllTasks(after.ProjectId).Where(t => t.ParentId == after.Id)
            .Select(t => ProjectRules.Stamped(null, t with { Id = ProjectRules.NewId(), ParentId = next.Id, Done = false, Created = default, Changed = default,
                Fields = new Dictionary<string, Stamp>() }, now)));
        return made;
    }

    /// <summary>A new project from a template, starting on a day, with everything in it, at the end of the list.</summary>
    public Project AddFromTemplate(ProjectTemplate template, string name, DateOnly start)
    {
        var now = Now();
        var (project, headings, tasks) = ProjectRules.FromTemplate(template, name, start, ProjectRules.NextOrder(store.ListProjects().Select(p => p.Order)));
        var saved = ProjectRules.Stamped(null, project, now);
        store.SaveProjectWork([saved], headings.Select(h => ProjectRules.Stamped(null, h, now)), tasks.Select(t => ProjectRules.Stamped(null, t, now)));
        Changed?.Invoke(null);
        return saved;
    }

    /// <summary>A project as a template, to start others from.</summary>
    public ProjectTemplate TemplateOf(string projectId, string name) => ProjectRules.TemplateOf(name, Headings(projectId), AllTasks(projectId));

    /// <summary>Deletes a task with its subtasks. Returns what went, for Undo.</summary>
    public ProjectWork? DeleteTask(string id)
    {
        if (GetTask(id) is not { } task)
            return null;
        var now = Now();
        var gone = new List<ProjectTask> { task };
        gone.AddRange(AllTasks(task.ProjectId).Where(t => t.ParentId == id));
        store.SaveProjectWork([], [], gone.Select(t => Remove(t, now)));
        Changed?.Invoke(task.ProjectId);
        return new ProjectWork([], [], gone);
    }

    /// <summary>
    /// Undo: puts back what a delete took away, as it was before, with the restore recorded as a change. What was
    /// changed in the meantime by the delete (tasks moved off a deleted heading) goes back too.
    /// </summary>
    public void Restore(ProjectWork work)
    {
        var now = Now();
        store.SaveProjectWork(
            work.Projects.Select(p => Back(store.GetProject(p.Id), p, now)),
            work.Headings.Select(h => Back(store.GetHeading(h.Id), h, now)),
            work.Tasks.Select(t => Back(store.GetProjectTask(t.Id), t, now)));
        Changed?.Invoke(work.Projects.Count > 0 ? null : work.Headings.FirstOrDefault()?.ProjectId ?? work.Tasks.FirstOrDefault()?.ProjectId);
    }

    // A deleted marker: who deleted it and when, kept so the deletion can sync.
    private static Project Remove(Project p, Stamp now) => p with { Deleted = now, Changed = now, Fields = With(p.Fields, now) };
    private static Heading Remove(Heading h, Stamp now) => h with { Deleted = now, Changed = now, Fields = With(h.Fields, now) };
    private static ProjectTask Remove(ProjectTask t, Stamp now) => t with { Deleted = now, Changed = now, Fields = With(t.Fields, now) };

    private static Project Back(Project? stored, Project was, Stamp now)
    {
        var back = ProjectRules.Stamped(stored, was, now);
        return back with { Deleted = null, Changed = now, Fields = With(back.Fields, now) };
    }

    private static Heading Back(Heading? stored, Heading was, Stamp now)
    {
        var back = ProjectRules.Stamped(stored, was, now);
        return back with { Deleted = null, Changed = now, Fields = With(back.Fields, now) };
    }

    private static ProjectTask Back(ProjectTask? stored, ProjectTask was, Stamp now)
    {
        var back = ProjectRules.Stamped(stored, was, now);
        return back with { Deleted = null, Changed = now, Fields = With(back.Fields, now) };
    }

    private static Dictionary<string, Stamp> With(IReadOnlyDictionary<string, Stamp> fields, Stamp now) =>
        new(fields) { ["deleted"] = now };
}

/// <summary>What a delete took away, or moved, so Undo can put it back.</summary>
public sealed record ProjectWork(IReadOnlyList<Project> Projects, IReadOnlyList<Heading> Headings, IReadOnlyList<ProjectTask> Tasks);
