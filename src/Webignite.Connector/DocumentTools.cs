using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Webignite.Core.Documents;
using Webignite.Data;

namespace Webignite.Connector;

/// <summary>
/// Quote and invoice tools for Claude and ChatGPT. They can look documents up and prepare new ones, which
/// wait under To approve. Nothing reaches Xero or a customer until Eddie approves it in the app.
/// </summary>
[McpServerToolType]
public sealed class DocumentTools(AssistantDocuments documents)
{
    [McpServerTool(Name = "list_documents", Title = "List quotes and invoices", ReadOnly = true, OpenWorld = false)]
    [Description("Lists quotes and invoices kept in Webignite, newest first, with their lines, totals and where they stand "
        + "(waiting for Eddie, draft in Xero, approved, emailed).")]
    public string ListDocuments(
        [Description("quote or invoice. Leave out for both.")] string? kind = null,
        [Description("Part of the customer's name, e.g. Karoo.")] string? customer = null,
        [Description("How many to return, 1 to 100. Default 20.")] int limit = 20) =>
        Json(documents.List(kind, customer, limit));

    [McpServerTool(Name = "list_price_list", Title = "Eddie's price list", ReadOnly = true, OpenWorld = false)]
    [Description("Eddie's standard packages and items, each with its usual lines (description, quantity, unit price in Rand). "
        + "Use these lines and prices when preparing a quote for one of these packages, unless Eddie says otherwise.")]
    public string ListPriceList() =>
        Json(documents.Prices().Select(p => new
        {
            p.Name,
            Total = p.Total,
            Lines = p.Lines.Select(l => new { l.Description, l.Quantity, UnitPrice = l.UnitPrice }),
        }));

    [McpServerTool(Name = "prepare_document", Title = "Prepare a quote or invoice", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Prepares a quote or invoice for Eddie to check under To approve in Webignite. It does not go to Xero and is not "
        + "sent to the customer; Eddie approves it in the app. Amounts are in Rand with no VAT (Webignite is not VAT registered). "
        + "The date and due or expiry date are set from Eddie's settings. Call it once per document; calling again makes another.")]
    public string PrepareDocument(
        [Description("quote or invoice.")] string kind,
        [Description("The customer's name as it is in Xero, e.g. Karoo Coffee Roasters.")] string customer,
        [Description("The lines. Each has a description, and for priced lines a quantity and unit_price in Rand. A line with no price is a heading or note.")] DraftLineInput[] lines,
        [Description("The customer's email address, if known. Leave out to use the one saved on the client in Webignite.")] string? customer_email = null,
        [Description("Reference shown on the document, e.g. Website November.")] string? reference = null,
        [Description("Notes for the client printed on the document.")] string? notes = null,
        McpServer? server = null)
    {
        try
        {
            return Json(documents.Draft(kind, customer,
                lines.Select(l => new AssistantDocuments.DraftLine(l.Description, l.Quantity, l.UnitPrice)).ToList(),
                Source(server), customer_email, reference, notes));
        }
        catch (ArgumentException e)
        {
            throw new McpException(e.Message);
        }
    }

    public sealed record DraftLineInput(
        [property: Description("What was done or sold.")] string Description,
        [property: Description("How many, e.g. 1 or 3.5 hours. Leave out for a text-only line.")] decimal? Quantity = null,
        [property: Description("Price per unit in Rand, without VAT. Leave out for a text-only line.")] decimal? UnitPrice = null);

    private static DocumentSource Source(McpServer? server)
    {
        var name = server?.ClientInfo?.Name ?? "";
        return name.Contains("openai", StringComparison.OrdinalIgnoreCase) || name.Contains("chatgpt", StringComparison.OrdinalIgnoreCase)
            ? DocumentSource.ChatGpt
            : DocumentSource.Claude;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DocumentJson.Options);
}
