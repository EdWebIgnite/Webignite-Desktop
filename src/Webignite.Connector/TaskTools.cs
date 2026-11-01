using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;
using Webignite.Data;

namespace Webignite.Connector;

/// <summary>Eddie's to-do list for Claude and ChatGPT: read it, and add to it. Ticking off stays with Eddie.</summary>
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
        + "there, nothing new is added. You can't tick off, change or delete to-dos.")]
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

    private static TaskSource Source(McpServer? server)
    {
        var name = server?.ClientInfo?.Name ?? "";
        return name.Contains("openai", StringComparison.OrdinalIgnoreCase) || name.Contains("chatgpt", StringComparison.OrdinalIgnoreCase)
            ? TaskSource.ChatGpt
            : TaskSource.Claude;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DocumentJson.Options);
}
