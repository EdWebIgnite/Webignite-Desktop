using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Webignite.Core.Documents;
using Webignite.Services;

namespace Webignite.Connector;

/// <summary>
/// Planner for Claude and ChatGPT: projects, tasks, headings, custom fields, dependencies, links, status updates
/// and comments. They work as Eddie would in the app, and never delete anything.
/// </summary>
[McpServerToolType]
public sealed class PlannerTools(AssistantPlanner planner)
{
    private const string ProjectHint = "The project's name (or part of it, if only one matches) or its id.";
    private const string TaskHint = "The task's title (or part of it, if only one matches) or its id.";
    private const string DateHint = "As yyyy-MM-dd. Use none to clear it.";
    private const string AssigneeHint = "me (Eddie), partner, or no one.";

    // Reading

    [McpServerTool(Name = "list_projects", Title = "List projects", ReadOnly = true, OpenWorld = false)]
    [Description("Eddie's projects in Planner: name, client, status (active, on hold, done), health, due date, whether shared with "
        + "the partner, and how many tasks are open, done and late.")]
    public string ListProjects([Description("true to include finished projects. Default false.")] bool include_done = false) =>
        Json(planner.ListProjects(include_done));

    [McpServerTool(Name = "get_project", Title = "Get a project", ReadOnly = true, OpenWorld = false)]
    [Description("One project in full: description, headings (sections), custom fields with their options, links, the latest "
        + "status update and every top-level task.")]
    public string GetProject([Description(ProjectHint)] string project) => Run(() => planner.GetProject(project));

    [McpServerTool(Name = "list_tasks", Title = "List tasks", ReadOnly = true, OpenWorld = false)]
    [Description("Tasks across the projects still going, or in one project, soonest due first. Each has its heading, assignee, "
        + "start and due dates, priority, tags, custom field values and what it is waiting on.")]
    public string ListTasks(
        [Description("Only this project. " + ProjectHint)] string? project = null,
        [Description("me, partner, no one, or leave out for anyone.")] string? assignee = null,
        [Description("true to include finished tasks. Default false.")] bool include_done = false,
        [Description("Only tasks due on or before this day, as yyyy-MM-dd.")] string? due_before = null,
        [Description("How many to return, 1 to 500. Default 100.")] int limit = 100) =>
        Run(() => planner.ListTasks(project, assignee, include_done, due_before, limit));

    [McpServerTool(Name = "get_task", Title = "Get a task", ReadOnly = true, OpenWorld = false)]
    [Description("One task in full: notes, subtasks, what it waits on and what waits on it, comments and attached files.")]
    public string GetTask([Description(TaskHint)] string task, [Description("Its project, which helps when titles repeat.")] string? project = null) =>
        Run(() => planner.GetTask(task, project));

    [McpServerTool(Name = "list_templates", Title = "List project templates", ReadOnly = true, OpenWorld = false)]
    [Description("Templates a new project can start from: Eddie's own and the ones that come with Webignite.")]
    public string ListTemplates() => Json(planner.ListTemplates());

    // Projects

    [McpServerTool(Name = "create_project", Title = "Create a project", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Starts a project in Planner, blank or from a template (see list_templates). Check list_projects first so you "
        + "don't make the same one twice.")]
    public string CreateProject(
        [Description("What to call it, e.g. Karoo Coffee website.")] string name,
        [Description("A template's name or id, to start with its sections and tasks.")] string? template = null,
        [Description("The client it's for, by name.")] string? client = null,
        [Description("When it's due, as yyyy-MM-dd.")] string? due = null,
        [Description("The day the template's dates count from, as yyyy-MM-dd. Default today.")] string? start = null,
        [Description("true to share it with the partner.")] bool shared = false,
        [Description("What the project is about.")] string? description = null) =>
        Run(() => planner.CreateProject(name, template, client, due, shared, description, start));

    [McpServerTool(Name = "update_project", Title = "Change a project", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Changes a project. Only what you give changes. Set status to done to finish it (archive it); it can't be deleted.")]
    public string UpdateProject(
        [Description(ProjectHint)] string project,
        [Description("A new name.")] string? name = null,
        [Description("The client, by name, or none.")] string? client = null,
        [Description("The due date. " + DateHint)] string? due = null,
        [Description("active, on hold or done.")] string? status = null,
        [Description("true to share with the partner, false to keep it to Eddie.")] bool? shared = null,
        [Description("A new description, in place of the old one.")] string? description = null) =>
        Run(() => planner.UpdateProject(project, name, client, due, status, shared, description));

    [McpServerTool(Name = "post_status_update", Title = "Post a status update", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Posts a status update on a project, as on its Overview: how it's going and a short note.")]
    public string PostStatusUpdate(
        [Description(ProjectHint)] string project,
        [Description("on track, at risk or off track.")] string health,
        [Description("What's happening, a few lines.")] string text) =>
        Run(() => planner.PostStatus(project, health, text));

    [McpServerTool(Name = "add_project_link", Title = "Add a link to a project", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Adds a link to a project's key resources, e.g. the staging site or a shared folder.")]
    public string AddProjectLink(
        [Description(ProjectHint)] string project,
        [Description("The address, starting https://.")] string url,
        [Description("What to show for it.")] string? title = null) =>
        Run(() => planner.AddLink(project, url, title));

    [McpServerTool(Name = "add_heading", Title = "Add a section", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Adds a section (heading) to a project, which is also a column on its board.")]
    public string AddHeading([Description(ProjectHint)] string project, [Description("The section's name.")] string name) =>
        Run(() => planner.AddHeading(project, name));

    [McpServerTool(Name = "rename_heading", Title = "Rename a section", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Renames a section (heading) in a project.")]
    public string RenameHeading(
        [Description(ProjectHint)] string project,
        [Description("The section as it's called now.")] string heading,
        [Description("Its new name.")] string new_name) =>
        Run(() => planner.RenameHeading(project, heading, new_name));

    [McpServerTool(Name = "add_custom_field", Title = "Add a custom field", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Adds a custom field to a project's tasks: a choice with options, text, a number or a date.")]
    public string AddCustomField(
        [Description(ProjectHint)] string project,
        [Description("The field's name, e.g. Stage.")] string name,
        [Description("choice, text, number or date.")] string kind,
        [Description("For a choice: the options, in order.")] string[]? options = null) =>
        Run(() => planner.AddField(project, name, kind, options));

    // Tasks

    [McpServerTool(Name = "add_task", Title = "Add a task", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Adds a task to a project, under a section or as a subtask of another task.")]
    public string AddTask(
        [Description(ProjectHint)] string project,
        [Description("What needs doing, short.")] string title,
        [Description("The section to put it under.")] string? heading = null,
        [Description("Who it's for: " + AssigneeHint)] string? assignee = null,
        [Description("When it's due, as yyyy-MM-dd.")] string? due = null,
        [Description("When work starts, as yyyy-MM-dd.")] string? start = null,
        [Description("Any detail.")] string? notes = null,
        [Description("high, normal or low.")] string? priority = null,
        [Description("Tags, e.g. design, copy.")] string[]? tags = null,
        [Description("Make it a subtask of this task.")] string? parent_task = null,
        [Description("true for a milestone.")] bool milestone = false) =>
        Run(() => planner.AddTask(project, title, heading, assignee, due, start, notes, priority, tags, parent_task, milestone));

    [McpServerTool(Name = "update_task", Title = "Change a task", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Changes a task, ticks it off (done true) or opens it again (done false), or moves it to another project. "
        + "Only what you give changes. Tasks can't be deleted.")]
    public string UpdateTask(
        [Description(TaskHint)] string task,
        [Description("Its project, which helps when titles repeat.")] string? project = null,
        [Description("A new title.")] string? title = null,
        [Description("New notes, in place of the old ones.")] string? notes = null,
        [Description("The section to move it to, or none.")] string? heading = null,
        [Description(AssigneeHint)] string? assignee = null,
        [Description("The due date. " + DateHint)] string? due = null,
        [Description("The start date. " + DateHint)] string? start = null,
        [Description("high, normal or low.")] string? priority = null,
        [Description("The tags it should have, in place of the old ones.")] string[]? tags = null,
        [Description("true to tick it off, false to open it again.")] bool? done = null,
        [Description("true to make it a milestone, false for an ordinary task.")] bool? milestone = null,
        [Description("Move it to this project.")] string? move_to_project = null) =>
        Run(() => planner.UpdateTask(task, project, title, notes, heading, assignee, due, start, priority, tags, done, milestone, move_to_project));

    [McpServerTool(Name = "add_comment", Title = "Comment on a task", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Adds a comment to a task, signed as from you.")]
    public string AddComment(
        [Description(TaskHint)] string task,
        [Description("The comment.")] string text,
        [Description("Its project, which helps when titles repeat.")] string? project = null,
        McpServer? server = null) =>
        Run(() => planner.AddComment(task, text, Assistant.Name(server), project));

    [McpServerTool(Name = "set_task_field", Title = "Fill in a custom field", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sets a custom field on a task: an option's name for a choice, text, a number like 1500.50, or a date as yyyy-MM-dd. "
        + "Empty clears it.")]
    public string SetTaskField(
        [Description(TaskHint)] string task,
        [Description("The field's name.")] string field,
        [Description("The value.")] string value,
        [Description("Its project, which helps when titles repeat.")] string? project = null) =>
        Run(() => planner.SetField(task, field, value, project));

    [McpServerTool(Name = "set_dependency", Title = "Set what a task waits on", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Makes a task wait on another task in the same project (it can't start until that one is done), or stops it waiting.")]
    public string SetDependency(
        [Description("The task that waits. " + TaskHint)] string task,
        [Description("The task it waits on.")] string waits_on,
        [Description("true to make it wait, false to stop it waiting. Default true.")] bool waits = true,
        [Description("Their project, which helps when titles repeat.")] string? project = null) =>
        Run(() => planner.SetDependency(task, waits_on, waits, project));

    private static string Run<T>(Func<T> work)
    {
        try
        {
            return Json(work());
        }
        catch (ArgumentException e)
        {
            throw new McpException(e.Message);
        }
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DocumentJson.Options);
}

/// <summary>Which assistant is asking, from what its app says about itself.</summary>
internal static class Assistant
{
    public static bool IsChatGpt(McpServer? server)
    {
        var name = server?.ClientInfo?.Name ?? "";
        return name.Contains("openai", StringComparison.OrdinalIgnoreCase) || name.Contains("chatgpt", StringComparison.OrdinalIgnoreCase);
    }

    public static string Name(McpServer? server) => IsChatGpt(server) ? "ChatGPT" : "Claude";
}
