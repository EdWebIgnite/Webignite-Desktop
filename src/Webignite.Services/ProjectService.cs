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

    /// <summary>Tells the open pages that projects changed outside this app, e.g. Claude or ChatGPT through the connector.</summary>
    public void ChangedElsewhere() => Changed?.Invoke(null);

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

    /// <summary>Posts a status update on the project's Overview; the header's status follows it.</summary>
    public (Project? Saved, IReadOnlyList<string> Problems) PostStatus(string id, ProjectHealth health, string text)
    {
        if (Get(id) is not { } current)
            return (null, ["The project is gone."]);
        var (changed, problem) = ProjectOverview.Post(current, health, text, Now(), ProjectRules.NewId());
        return changed is null ? (null, [problem]) : Change(id, p => p with { Health = changed.Health, Updates = changed.Updates });
    }

    /// <summary>Adds a key resource to the project's Overview.</summary>
    public (Project? Saved, IReadOnlyList<string> Problems) AddLink(string id, string title, string url)
    {
        if (Get(id) is not { } current)
            return (null, ["The project is gone."]);
        var (changed, problem) = ProjectOverview.AddLink(current, title, url, ProjectRules.NewId());
        return changed is null ? (null, [problem]) : Change(id, p => p with { Links = changed.Links });
    }

    public (Project? Saved, IReadOnlyList<string> Problems) RemoveLink(string id, string linkId) => Change(id, p => ProjectOverview.RemoveLink(p, linkId));

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
    public IReadOnlyList<ProjectTask> Mine(string meId)
    {
        BringBackIfNewDay();
        return ProjectRules.Mine(store.ListProjectTasks(), store.ListProjects(), meId);
    }

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

    /// <summary>
    /// Deletes a heading; its tasks stay in the project, under no heading. Returns what changed, for Undo: the
    /// heading, and which tasks were moved off it.
    /// </summary>
    public ProjectWork? DeleteHeading(string id)
    {
        if (store.GetHeading(id) is not { Deleted: null } heading)
            return null;
        var now = Now();
        var moved = AllTasks(heading.ProjectId).Where(t => t.HeadingId == id).ToList();
        store.SaveProjectWork([], [Remove(heading, now)], moved.Select(t => ProjectRules.Stamped(t, t with { HeadingId = null }, now)));
        Changed?.Invoke(heading.ProjectId);
        return new ProjectWork([], [heading], []) { MovedOff = [.. moved.Select(t => (t.Id, id))] };
    }

    // Tasks

    /// <summary>A project's tasks and subtasks, not deleted, in their order.</summary>
    public IReadOnlyList<ProjectTask> AllTasks(string projectId)
    {
        BringBackIfNewDay();
        return [.. store.ListProjectTasks().Where(t => t.ProjectId == projectId && t.Deleted is null).OrderBy(t => t.Order)];
    }

    public ProjectTask? GetTask(string id) => store.GetProjectTask(id) is { Deleted: null } task ? task : null;

    /// <summary>A new task (or subtask) at the end of its heading, or its task's subtasks; at the top with <paramref name="first"/>.</summary>
    public (ProjectTask? Saved, IReadOnlyList<string> Problems) AddTask(ProjectTask typed, bool first = false)
    {
        if (Get(typed.ProjectId) is null)
            return (null, ["The project is gone."]);
        var siblings = AllTasks(typed.ProjectId).Where(t => t.ParentId == typed.ParentId && (typed.ParentId is not null || t.HeadingId == typed.HeadingId));
        var orders = siblings.Select(t => t.Order);
        var task = typed with { Title = typed.Title.Trim(), Order = first ? ProjectRules.FirstOrder(orders) : ProjectRules.NextOrder(orders) };
        var problems = ProjectRules.Check(task, task.ParentId is { } parent ? GetTask(parent) : null);
        if (problems.Count > 0)
            return (null, problems);
        var now = Now();
        var saved = store.SaveProjectTask(ProjectRules.Stamped(null, TaskTalk.Followed(null, task, now.By), now));
        Record(null, saved, now);
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
        if (TickedOffForNow(current, changed, now) is { } again)
            return (again, []);
        // Unticked by hand while waiting to come back: it is back now, on the date it was moved to.
        if (current.Done && !changed.Done)
            changed = changed with { BackOn = null };
        var saved = ProjectRules.Stamped(current, TaskTalk.Followed(current, changed, now.By), now);
        store.SaveProjectWork([], [], [saved, .. subtasks]);
        Record(current, saved, now);
        Changed?.Invoke(current.ProjectId);
        return (saved, []);
    }

    /// <summary>
    /// A repeating task just ticked off stays the one task: it shows as done with its next due date until the day
    /// it comes back (<see cref="BringBackRepeating"/>), and its history keeps that it was done and for which day. Null when the
    /// change isn't that (the task doesn't repeat, is a subtask, or wasn't ticked off by it).
    /// </summary>
    private ProjectTask? TickedOffForNow(ProjectTask before, ProjectTask ticked, Stamp now)
    {
        if (before.Done || !ticked.Done || ticked.ParentId is not null || ticked.Repeat == TaskRepeat.None)
            return null;
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var all = AllTasks(before.ProjectId);
        var again = ProjectRules.ForNextTime(ticked, all, today);
        if (again.Count == 0)
            return null;
        var saved = ProjectRules.Stamped(before, TaskTalk.Followed(before, again[0], now.By), now);
        var subtasks = again.Skip(1).Select(t => ProjectRules.Stamped(all.First(s => s.Id == t.Id), t, now));
        store.SaveProjectWork([], [], [saved, .. subtasks]);
        // Whatever else the change did (a rename, a new assignee) is kept as usual; the tick and the date it moved
        // the task to are one line of their own, not "marked complete" and "changed the due date".
        var untouched = ticked with { Done = before.Done, Due = before.Due, BackOn = before.BackOn };
        var headings = Headings(saved.ProjectId).ToDictionary(h => h.Id, h => h.Name);
        foreach (var (kind, value) in TaskTalk.Changes(before, untouched, id => id is null ? null : headings.GetValueOrDefault(id), id => Get(id)?.Name))
            store.SaveActivity(new TaskActivity(ProjectRules.NewId(), saved.Id, saved.ProjectId, now, kind, value));
        store.SaveActivity(new TaskActivity(ProjectRules.NewId(), saved.Id, saved.ProjectId, now, ActivityKind.DoneThisTime, TaskTalk.DoneThisTimeValue(before.Due, saved.Due!.Value)));
        Changed?.Invoke(before.ProjectId);
        return saved;
    }

    private DateOnly broughtBackFor;

    /// <summary>
    /// Unticks the repeating tasks whose day to come back has arrived, with their subtasks, so they are on the
    /// lists again for their next time. Returns how many came back. Cheap when none is due.
    /// </summary>
    public int BringBackRepeating(bool tell = true)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        broughtBackFor = today;
        var all = store.ListProjectTasks();
        var back = ProjectRules.BackToday(all, today);
        if (back.Count == 0)
            return 0;
        var now = Now();
        var byId = all.ToDictionary(t => t.Id);
        store.SaveProjectWork([], [], [.. back.Select(t => ProjectRules.Stamped(byId[t.Id], t, now))]);
        if (tell)
        {
            foreach (var project in back.Select(t => t.ProjectId).Distinct())
                Changed?.Invoke(project);
        }
        return back.Count(t => t.ParentId is null);
    }

    // The first read of tasks on a new day brings back what is due, so a list never shows yesterday's state.
    private void BringBackIfNewDay()
    {
        if (broughtBackFor != DateOnly.FromDateTime(clock.GetLocalNow().DateTime))
            BringBackRepeating(tell: false);
    }

    /// <summary>Adds a custom field to a project, as Asana's; a choice field takes its options from what's typed.</summary>
    public (Project? Saved, IReadOnlyList<string> Problems) AddField(string projectId, string name, FieldKind kind, IEnumerable<string> options)
    {
        if (Get(projectId) is not { } current)
            return (null, ["The project is gone."]);
        var (changed, problem) = CustomFields.Add(current, name, kind, options, ProjectRules.NewId);
        return changed is null ? (null, [problem]) : Change(projectId, p => p with { CustomFields = changed.CustomFields });
    }

    /// <summary>Takes a custom field off a project; tasks keep their values, unseen, so Undo can bring it back whole.</summary>
    public (Project? Saved, IReadOnlyList<string> Problems) RemoveField(string projectId, string fieldId) =>
        Change(projectId, p => CustomFields.Remove(p, fieldId));

    /// <summary>Sets a task's value for one of its project's fields from what's typed; empty clears it.</summary>
    public (ProjectTask? Saved, IReadOnlyList<string> Problems) SetFieldValue(string taskId, string fieldId, string typed, IFormatProvider culture)
    {
        if (GetTask(taskId) is not { } task || Get(task.ProjectId) is not { } project)
            return (null, ["The task is gone."]);
        if (project.CustomFields.FirstOrDefault(f => f.Id == fieldId) is not { } field)
            return (null, ["That field isn't on the project any more."]);
        var (value, problem) = CustomFields.Read(field, typed, culture);
        return problem.Length > 0 ? (null, [problem]) : ChangeTask(taskId, t => CustomFields.WithValue(t, fieldId, value));
    }

    /// <summary>Makes a task wait on another in its project, as Asana's dependencies; says why when it can't.</summary>
    public (ProjectTask? Saved, IReadOnlyList<string> Problems) AddDependency(string id, string blockerId)
    {
        if (GetTask(id) is not { } task)
            return (null, ["The task is gone."]);
        var problem = TaskDependencies.Problem(task, blockerId, AllTasks(task.ProjectId));
        return problem.Length > 0 ? (null, [problem]) : ChangeTask(id, t => TaskDependencies.Add(t, blockerId));
    }

    public (ProjectTask? Saved, IReadOnlyList<string> Problems) RemoveDependency(string id, string blockerId) =>
        ChangeTask(id, t => TaskDependencies.Remove(t, blockerId));

    /// <summary>
    /// Moves a task to another project, as when it's dropped on the project in the sidebar: it goes last among the
    /// tasks under no heading there, and takes its subtasks along. A subtask moved on its own becomes a task.
    /// Waiting only works within a project, so what it waited on stays behind, and tasks left behind stop
    /// waiting on it; each of those changes shows on the task's feed.
    /// </summary>
    public (ProjectTask? Saved, IReadOnlyList<string> Problems) MoveTask(string id, string projectId)
    {
        if (GetTask(id) is not { } current)
            return (null, ["The task is gone."]);
        if (Get(projectId) is null)
            return (null, ["The project is gone."]);
        if (current.ProjectId == projectId)
            return (current, []);
        var going = TaskDependencies.Going(current, AllTasks(current.ProjectId));
        // Before it goes: unhook it from the tasks staying behind, both ways, as changes to each as stored now.
        foreach (var task in AllTasks(current.ProjectId).Where(t => !ReferenceEquals(TaskDependencies.Unhooked(t, going), t)))
            ChangeTask(task.Id, t => TaskDependencies.Unhooked(t, going));
        if (GetTask(id) is not { } unhooked)
            return (null, ["The task is gone."]);
        current = unhooked;
        var now = Now();
        var loose = AllTasks(projectId).Where(t => t.ParentId is null && t.HeadingId is null);
        var moved = ProjectRules.Stamped(current, current with { ProjectId = projectId, HeadingId = null, ParentId = null, Order = ProjectRules.NextOrder(loose.Select(t => t.Order)) }, now);
        var subtasks = current.ParentId is null
            ? AllTasks(current.ProjectId).Where(t => t.ParentId == id).Select(t => ProjectRules.Stamped(t, t with { ProjectId = projectId, HeadingId = null }, now)).ToList()
            : [];
        store.SaveProjectWork([], [], [moved, .. subtasks]);
        Record(current, moved, now);
        Changed?.Invoke(current.ProjectId);
        Changed?.Invoke(projectId);
        return (moved, []);
    }

    // Comments and the feed, as Asana's. In shared projects they sync with the rest (see SyncService).

    /// <summary>A task's comments, not deleted, oldest first.</summary>
    public IReadOnlyList<TaskComment> Comments(string taskId) =>
        [.. store.ListComments(taskId).Where(c => c.Deleted is null).OrderBy(c => c.Created.At)];

    /// <summary>What happened to a task, oldest first.</summary>
    public IReadOnlyList<TaskActivity> Activity(string taskId) => [.. store.ListActivity(taskId).OrderBy(a => a.At.At)];

    /// <summary>How many comments each task has, for the speech bubbles in the list.</summary>
    public IReadOnlyDictionary<string, int> CommentCounts() => store.CommentCounts();

    /// <summary>A comment on a task, by me, now. The problem instead when there's nothing in it or the task is gone.</summary>
    public (TaskComment? Saved, string? Problem) AddComment(string taskId, string text)
    {
        if (TaskTalk.Check(text) is { Length: > 0 } problem)
            return (null, problem);
        if (GetTask(taskId) is not { } task)
            return (null, "The task is gone.");
        var now = Now();
        var comment = new TaskComment { Id = ProjectRules.NewId(), TaskId = taskId, ProjectId = task.ProjectId, Text = text.Trim(), Mentions = MentionsIn(text) };
        var saved = store.SaveComment(ProjectRules.Stamped(null, comment, now));
        FollowFor(task, saved, now);
        Changed?.Invoke(task.ProjectId);
        return (saved, null);
    }

    /// <summary>A comment's words changed, as stored now. Anyone newly mentioned follows the task.</summary>
    public TaskComment? EditComment(string id, string text)
    {
        if (store.GetComment(id) is not { Deleted: null } current || TaskTalk.Check(text).Length > 0)
            return null;
        var now = Now();
        var saved = store.SaveComment(ProjectRules.Stamped(current, current with { Text = text.Trim(), Mentions = MentionsIn(text) }, now));
        if (GetTask(saved.TaskId) is { } task)
            FollowFor(task, saved, now);
        Changed?.Invoke(current.ProjectId);
        return saved;
    }

    private IReadOnlyList<string> MentionsIn(string text) => TaskTalk.Mentions(text, store.GetSetting<TeamSettings>("team") ?? new TeamSettings());

    // Commenting, and being mentioned, makes you follow the task. No activity line: nobody needs to read it.
    private void FollowFor(ProjectTask task, TaskComment comment, Stamp now)
    {
        var followed = TaskTalk.FollowedFor(task, comment);
        if (!ReferenceEquals(followed, task))
            store.SaveProjectTask(ProjectRules.Stamped(task, followed, now));
    }

    /// <summary>I like, or stop liking, a task. Null when it's gone.</summary>
    public ProjectTask? LikeTask(string id, bool like) => Touch(id, t => t with { LikedBy = Sets.With(t.LikedBy, me(), like) });

    /// <summary>I follow, or stop following, a task: followers hear about it in Inbox. Null when it's gone.</summary>
    public ProjectTask? FollowTask(string id, bool follow) => Touch(id, t => t with { Followers = Sets.With(t.Followers, me(), follow) });

    // A change to who likes or follows a task: stamped as anything else, but not an activity line.
    private ProjectTask? Touch(string id, Func<ProjectTask, ProjectTask> change)
    {
        if (GetTask(id) is not { } current)
            return null;
        var changed = change(current);
        if (ReferenceEquals(changed.LikedBy, current.LikedBy) && ReferenceEquals(changed.Followers, current.Followers))
            return current;
        var saved = store.SaveProjectTask(ProjectRules.Stamped(current, changed, Now()));
        Changed?.Invoke(current.ProjectId);
        return saved;
    }

    // Messages, as Asana's: a project's conversations, with replies, @mentions and likes. Synced in shared projects.

    /// <summary>A project's conversations, the liveliest first, each with its replies.</summary>
    public IReadOnlyList<MessageThread> Messages(string projectId) => MessageRules.Threads(store.ListMessages(projectId));

    /// <summary>A new conversation on a project, or a reply to one (<paramref name="replyTo"/>), by me, now.</summary>
    public (ProjectMessage? Saved, string? Problem) PostMessage(string projectId, string subject, string text, string? replyTo = null)
    {
        if (MessageRules.Check(subject, text, replyTo is not null) is { Length: > 0 } problem)
            return (null, problem);
        if (Get(projectId) is null)
            return (null, "The project is gone.");
        if (replyTo is not null && store.GetMessage(replyTo) is not { Deleted: null })
            return (null, "That message is gone.");
        var message = new ProjectMessage
        {
            Id = ProjectRules.NewId(), ProjectId = projectId, ReplyTo = replyTo, Subject = replyTo is null ? subject.Trim() : "",
            Text = text.Trim(), Mentions = MentionsIn(text),
        };
        var saved = store.SaveMessage(ProjectRules.Stamped(null, message, Now()));
        Changed?.Invoke(projectId);
        return (saved, null);
    }

    /// <summary>A message's words changed, as stored now.</summary>
    public ProjectMessage? EditMessage(string id, string subject, string text)
    {
        if (store.GetMessage(id) is not { Deleted: null } current || MessageRules.Check(subject, text, current.ReplyTo is not null).Length > 0)
            return null;
        var changed = current with { Subject = current.ReplyTo is null ? subject.Trim() : "", Text = text.Trim(), Mentions = MentionsIn(text) };
        var saved = store.SaveMessage(ProjectRules.Stamped(current, changed, Now()));
        Changed?.Invoke(current.ProjectId);
        return saved;
    }

    /// <summary>Deletes a message (and so, on screen, its replies), leaving a marker to sync; returns it, for Undo.</summary>
    public ProjectMessage? DeleteMessage(string id)
    {
        if (store.GetMessage(id) is not { Deleted: null } current)
            return null;
        store.SaveMessage(current with { Deleted = Now() });
        Changed?.Invoke(current.ProjectId);
        return current;
    }

    public void RestoreMessage(ProjectMessage message)
    {
        store.SaveMessage(message with { Deleted = null });
        Changed?.Invoke(message.ProjectId);
    }

    /// <summary>I like, or stop liking, a message. Null when it's gone.</summary>
    public ProjectMessage? LikeMessage(string id, bool like)
    {
        if (store.GetMessage(id) is not { Deleted: null } current)
            return null;
        var likes = Sets.With(current.LikedBy, me(), like);
        if (ReferenceEquals(likes, current.LikedBy))
            return current;
        var saved = store.SaveMessage(ProjectRules.Stamped(current, current with { LikedBy = likes }, Now()));
        Changed?.Invoke(current.ProjectId);
        return saved;
    }

    /// <summary>I like, or stop liking, a comment. Null when it's gone.</summary>
    public TaskComment? LikeComment(string id, bool like)
    {
        if (store.GetComment(id) is not { Deleted: null } current)
            return null;
        var likes = Sets.With(current.LikedBy, me(), like);
        if (ReferenceEquals(likes, current.LikedBy))
            return current;
        var saved = store.SaveComment(ProjectRules.Stamped(current, current with { LikedBy = likes }, Now()));
        Changed?.Invoke(current.ProjectId);
        return saved;
    }

    /// <summary>Deletes a comment, leaving a marker so the deletion can sync; returns it, for Undo.</summary>
    public TaskComment? DeleteComment(string id)
    {
        if (store.GetComment(id) is not { Deleted: null } current)
            return null;
        store.SaveComment(current with { Deleted = Now() });
        Changed?.Invoke(current.ProjectId);
        return current;
    }

    public void RestoreComment(TaskComment comment)
    {
        store.SaveComment(comment with { Deleted = null });
        Changed?.Invoke(comment.ProjectId);
    }

    // What changed, written to the task's feed.
    private void Record(ProjectTask? before, ProjectTask after, Stamp now)
    {
        var headings = Headings(after.ProjectId).ToDictionary(h => h.Id, h => h.Name);
        foreach (var (kind, value) in TaskTalk.Changes(before, after, id => id is null ? null : headings.GetValueOrDefault(id), id => Get(id)?.Name))
            store.SaveActivity(new TaskActivity(ProjectRules.NewId(), after.Id, after.ProjectId, now, kind, value));
        // A subtask added shows on its task's feed too, as in Asana.
        if (before is null && after.ParentId is { } parent)
            store.SaveActivity(new TaskActivity(ProjectRules.NewId(), parent, after.ProjectId, now, ActivityKind.SubtaskAdded, after.Title));
    }

    /// <summary>A new project from a template, starting on a day, with everything in it, at the end of the list.</summary>
    public Project AddFromTemplate(ProjectTemplate template, string name, DateOnly start)
    {
        var now = Now();
        var (project, headings, tasks) = ProjectRules.FromTemplate(template, name, start, ProjectRules.NextOrder(store.ListProjects().Select(p => p.Order)));
        var saved = ProjectRules.Stamped(null, project, now);
        store.SaveProjectWork([saved], headings.Select(h => ProjectRules.Stamped(null, h, now)), tasks.Select(t => ProjectRules.Stamped(null, TaskTalk.Followed(null, t, now.By), now)));
        Changed?.Invoke(null);
        return saved;
    }

    /// <summary>A project as a template, to start others from.</summary>
    public ProjectTemplate TemplateOf(string projectId, string name) =>
        ProjectRules.TemplateOf(name, Headings(projectId), AllTasks(projectId), Get(projectId)?.CustomFields);

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
    /// Undo: puts back what a delete took away, as it was before, with the restore recorded as a change. Tasks the
    /// delete moved off a heading go back onto it, each as it's stored now, and only while it's still under no
    /// heading, so whatever changed on them since stays.
    /// </summary>
    public void Restore(ProjectWork work)
    {
        var now = Now();
        store.SaveProjectWork(
            work.Projects.Select(p => Back(store.GetProject(p.Id), p, now)),
            work.Headings.Select(h => Back(store.GetHeading(h.Id), h, now)),
            work.Tasks.Select(t => Back(store.GetProjectTask(t.Id), t, now)));
        foreach (var (taskId, headingId) in work.MovedOff)
            if (store.GetHeading(headingId) is { Deleted: null } heading && GetTask(taskId) is { } task
                && !ReferenceEquals(ProjectRules.BackOnHeading(task, heading), task))
                ChangeTask(taskId, t => ProjectRules.BackOnHeading(t, heading));
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
public sealed record ProjectWork(IReadOnlyList<Project> Projects, IReadOnlyList<Heading> Headings, IReadOnlyList<ProjectTask> Tasks)
{
    /// <summary>Tasks a deleted heading's delete moved to no heading, with the heading they were under.</summary>
    public IReadOnlyList<(string TaskId, string HeadingId)> MovedOff { get; init; } = [];
}
