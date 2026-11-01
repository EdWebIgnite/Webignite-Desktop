using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Data;

/// <summary>
/// What Claude and ChatGPT can do with quotes and invoices: look them up, and prepare new ones that wait
/// under To approve. Nothing here talks to Xero; only Eddie's click in the app does that.
/// </summary>
public sealed class AssistantDocuments(LocalStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public sealed record DraftLine(string Description, decimal? Quantity = null, decimal? UnitPrice = null);

    public sealed record Prepared(string Id, string Kind, string Customer, string Date, string DueOrExpiry, string Total,
        IReadOnlyList<string> StillNeeded, string Message);

    public sealed record Listed(string Id, string Kind, string? Number, string Customer, string? Reference, string Date, string Total,
        string Status, IReadOnlyList<string> Lines);

    public Prepared Draft(string kind, string customer, IReadOnlyList<DraftLine> lines, DocumentSource source,
        string? customerEmail = null, string? reference = null, string? notes = null)
    {
        var parsed = kind.Trim().ToLowerInvariant() switch
        {
            "quote" => DocumentKind.Quote,
            "invoice" => DocumentKind.Invoice,
            _ => throw new ArgumentException("kind must be quote or invoice."),
        };
        if (string.IsNullOrWhiteSpace(customer))
            throw new ArgumentException("Say who it's for: customer is empty.");
        if (lines.Count == 0 || lines.All(l => string.IsNullOrWhiteSpace(l.Description)))
            throw new ArgumentException("Add at least one line with a description.");
        if (lines.Any(l => l.UnitPrice is < 0 || l.Quantity is < 0))
            throw new ArgumentException("Quantities and prices can't be negative. Use a separate credit note in Xero for refunds.");

        var name = customer.Trim();
        var client = store.ListClients().FirstOrDefault(c => XeroRules.Normalise(c.Name) == XeroRules.Normalise(name));
        var email = string.IsNullOrWhiteSpace(customerEmail)
            ? client?.Email?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            : customerEmail.Trim();

        var settings = store.GetSetting<DocumentSettings>("documents") ?? new DocumentSettings();
        var document = DocumentDefaults.Complete(new Document
        {
            Kind = parsed,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = client?.Name ?? name, Email = email },
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Lines = lines.Where(l => !string.IsNullOrWhiteSpace(l.Description))
                .Select(l => new Line
                {
                    Description = l.Description.Trim(),
                    Quantity = l.UnitPrice is null && l.Quantity is null ? null : l.Quantity ?? 1,
                    UnitPrice = l.UnitPrice,
                }).ToList(),
        }, settings, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));

        var stored = store.Save(new StoredDocument
        {
            Id = StoredDocument.NewId(),
            Document = document,
            Status = DocumentStatus.AwaitingApproval,
            Source = source,
        });
        var problems = DocumentValidator.Validate(document);
        return new Prepared(stored.Id, kind.Trim().ToLowerInvariant(), document.Customer.Name, document.Date?.ToString("yyyy-MM-dd") ?? "",
            document.DueOrExpiryDate?.ToString("yyyy-MM-dd") ?? "", Money.Rand(document.Total ?? 0), problems,
            "Saved in Webignite under To approve for Eddie to check. Nothing has gone to Xero, and it is not sent to the customer."
            + (problems.Count > 0 ? " It still needs: " + string.Join(" ", problems) : ""));
    }

    /// <summary>Eddie's standard packages and items, with their usual lines and prices.</summary>
    public IReadOnlyList<PriceEntry> Prices() => store.GetSetting<List<PriceEntry>>("price-list") ?? [];

    /// <summary>Quotes and invoices kept in Webignite, newest first.</summary>
    public IReadOnlyList<Listed> List(string? kind = null, string? customer = null, int limit = 20)
    {
        DocumentKind? only = kind?.Trim().ToLowerInvariant() switch
        {
            "quote" or "quotes" => DocumentKind.Quote,
            "invoice" or "invoices" => DocumentKind.Invoice,
            _ => null,
        };
        var wanted = XeroRules.Normalise(customer);
        return store.List(only)
            .Where(s => wanted.Length == 0 || XeroRules.Normalise(s.Document.Customer.Name).Contains(wanted, StringComparison.Ordinal))
            .Take(Math.Clamp(limit <= 0 ? 20 : limit, 1, 100))
            .Select(s => new Listed(s.Id, s.Document.Kind.ToString().ToLowerInvariant(), s.XeroNumber, s.Document.Customer.Name, s.Document.Reference,
                s.Document.Date?.ToString("yyyy-MM-dd") ?? "", Money.Rand(s.Document.Total ?? 0), StatusText(s),
                s.Document.Lines.Select(l => l.IsPriced ? $"{l.Description}: {l.Quantity:0.##} x {Money.Rand(l.UnitPrice ?? 0)}" : l.Description).ToList()))
            .ToList();
    }

    private static string StatusText(StoredDocument s) => s switch
    {
        { Emailed: not null } => "emailed to the customer",
        { Approved: not null } => "approved in Xero, not emailed",
        { XeroState: XeroWriteState.Verified } => "draft in Xero",
        { Status: DocumentStatus.AwaitingApproval } => "waiting for Eddie under To approve",
        { XeroId: not null } => "in Xero",
        _ => "draft on Eddie's PC",
    };
}
