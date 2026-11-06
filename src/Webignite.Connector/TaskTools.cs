using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;
using Webignite.Data;

namespace Webignite.Connector;

/// <summary>Eddie's to-do list for Claude and ChatGPT: read it, add to it, change and tick off. Never delete.</summary>
[McpServerToolType]
public sealed class TaskTools(AssistantTasks tasks)
{
    [McpServerTool(Name = "list_todos", Title = "List to-dos", ReadOnly = true, OpenWorld = false)]
    [Description("Eddie's to-do list in Webignite, soonest due first: title, notes, due date (yyyy-MM-dd), client, whether it's "
        + "overdue, and who added it (Eddie, Webignite, Claude or ChatGPT).")]
    public string ListTodos(
        [Description("true to include finished ones. Default false.")] bool include_done = false,
        [Description("Part of a client's name, e.g. Karoo, to see only theirs.")] string? customer = null,
        [Description("How many to return, 1 to 200. Default 50.")] int limit = 50) =>
        Json(tasks.List(include_done, customer, limit));

    [McpServerTool(Name = "add_todo", Title = "Add a to-do", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Adds a to-do to Eddie's list in Webignite, marked as from you. If an open to-do with the same title is already "
        + "there, nothing new is added.")]
    public string AddTodo(
        [Description("What needs doing, short, e.g. Send Karoo Coffee the sitemap.")] string title,
        [Description("When it's due, as yyyy-MM-dd. Leave out if there's no date.")] string? due = null,
        [Description("The client it's for, e.g. Karoo Coffee Roasters.")] string? customer = null,
        [Description("Any detail Eddie needs.")] string? notes = null,
        McpServer? server = null)
    {
        try
        {
            return Json(tasks.Add(title, Source(server), due, customer, notes));
        }
        catch (ArgumentException e)
        {
            throw new McpException(e.Message);
        }
    }

    [McpServerTool(Name = "update_todo", Title = "Change or tick off a to-do", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Changes a to-do on Eddie's list, ticks it off (done true) or opens it again (done false). Only what you give "
        + "changes. To-dos can't be deleted.")]
    public string UpdateTodo(
        [Description("The to-do's title (or part of it, if only one matches) or its id.")] string todo,
        [Description("A new title.")] string? title = null,
        [Description("The due date as yyyy-MM-dd, or none to clear it.")] string? due = null,
        [Description("New notes, in place of the old ones.")] string? notes = null,
        [Description("true to tick it off, false to open it again.")] bool? done = null)
    {
        try
        {
            return Json(tasks.Update(todo, title, due, notes, done));
        }
        catch (ArgumentException e)
        {
            throw new McpException(e.Message);
        }
    }

    private static TaskSource Source(McpServer? server) => Assistant.IsChatGpt(server) ? TaskSource.ChatGpt : TaskSource.Claude;

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DocumentJson.Options);
}
