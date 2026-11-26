using System.Globalization;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Data;

/// <summary>
/// What Claude and ChatGPT can do with quotes and invoices: look them up, prepare new ones that wait under
/// To approve, and change one that has not reached Xero yet. Nothing here talks to Xero; only Eddie's click in
/// the app does that.
/// </summary>
public sealed class AssistantDocuments(LocalStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    /// <param name="Item">The short item name, shown in Xero's Item column, e.g. WordPress setup.</param>
    public sealed record DraftLine(string Description, decimal? Quantity = null, decimal? UnitPrice = null, string? Item = null);

    public sealed record Prepared(string Id, string Kind, string Customer, string Date, string DueOrExpiry, string Total,
        IReadOnlyList<string> StillNeeded, string Message);

    /// <param name="CanChange">Whether change_document may change it: while it has not reached Xero, or is a draft there nobody has approved or emailed.</param>
    public sealed record Listed(string Id, string Kind, string? Number, string Customer, string? Reference, string Date, string Total,
        string Status, IReadOnlyList<string> Lines, bool CanChange = false, string? Notes = null, string? Discount = null);

    public Prepared Draft(string kind, string customer, IReadOnlyList<DraftLine> lines, DocumentSource source,
        string? customerEmail = null, string? reference = null, string? notes = null, decimal? discountPercent = null, decimal? discountAmount = null)
    {
        if (discountPercent is not null && discountAmount is not null)
            throw new ArgumentException("Give the discount as a percentage or an amount, not both.");
        if (discountPercent is <= 0 or > 100 || discountAmount is <= 0)
            throw new ArgumentException("A discount is a percentage up to 100 or an amount in Rand above nothing.");
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
        var client = Webignite.Core.Clients.ClientDirectory.Find(store.ListClients(), name);
        // A client already in Webignite keeps the address saved on them: an assistant (perhaps tricked by a mail)
        // can't send the invoice somewhere else. An address it gives is only used for someone not saved yet.
        var email = client is not null || string.IsNullOrWhiteSpace(customerEmail)
            ? Webignite.Core.Mail.ClientMail.First(client?.Email)
            : customerEmail.Trim();

        var settings = store.GetSetting<DocumentSettings>("documents") ?? new DocumentSettings();
        var document = DocumentDefaults.Complete(new Document
        {
            Kind = parsed,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = client?.Name ?? name, Email = email },
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            DocumentDiscountPercent = discountPercent,
            DocumentDiscountAmount = discountAmount is { } off ? Money.Cents(off) : null,
            Lines = Lines(lines),
        }, settings, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));

        var stored = store.Save(new StoredDocument
        {
            Id = StoredDocument.NewId(),
            Document = document,
            Status = DocumentStatus.AwaitingApproval,
            Source = source,
        });
        var problems = DocumentValidator.Validate(document);
        return new Prepared(stored.Id, kind.Trim().ToLowerInvariant(), document.Customer.Name, document.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            document.DueOrExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", Money.Rand(document.Total ?? 0), problems,
            "Saved in Webignite under To approve for Eddie to check. Nothing has gone to Xero, and it is not sent to the customer."
            + (problems.Count > 0 ? " It still needs: " + string.Join(" ", problems) : ""));
    }

    /// <summary>
    /// Changes a quote or invoice that has not reached Xero, or is a draft there that Webignite made and nobody has
    /// approved or emailed: its lines (all of them, when given), reference, notes or discount. What is left out stays
    /// as it is. The customer and their address are never changed here. It waits under To approve afterwards, marked
    /// as changed. A draft in Xero keeps Xero's copy until Eddie clicks Update draft in Xero; nothing goes to Xero here.
    /// </summary>
    public Prepared Change(string id, DocumentSource source, IReadOnlyList<DraftLine>? lines = null, string? reference = null, string? notes = null,
        decimal? discountPercent = null, decimal? discountAmount = null)
    {
        if (discountPercent is not null && discountAmount is not null)
            throw new ArgumentException("Give the discount as a percentage or an amount, not both.");
        if (discountPercent is < 0 or > 100 || discountAmount is < 0)
            throw new ArgumentException("A discount is a percentage up to 100 or an amount in Rand; 0 takes the discount off.");
        if (lines is not null && (lines.Count == 0 || lines.All(l => string.IsNullOrWhiteSpace(l.Description))))
            throw new ArgumentException("Give every line the document should have, at least one with a description. Leave lines out to keep them as they are.");
        if (lines?.Any(l => l.UnitPrice is < 0 || l.Quantity is < 0) == true)
            throw new ArgumentException("Quantities and prices can't be negative. Use a separate credit note in Xero for refunds.");
        if (lines is null && reference is null && notes is null && discountPercent is null && discountAmount is null)
            throw new ArgumentException("Say what to change: lines, reference, notes or a discount.");
        if (store.Get((id ?? "").Trim()) is not { } found)
            throw new ArgumentException("No quote or invoice has that id. Use list_documents to find it.");
        if (!Approvals.AssistantMayChange(found))
            throw new ArgumentException(found.Xero.State == XeroWriteState.Editing
                ? $"Eddie is changing that {Word(found)} in Webignite right now, so it wasn't changed. Tell Eddie what should change."
                : $"That {Word(found)} is already in Xero ({StatusText(found)}), so it can't be changed: once approved or emailed, Xero keeps it as it is. Tell Eddie what should change; a copy can be prepared with prepare_document.");
        var xeroDraft = !Approvals.AssistantMayChangeHere(found);

        string? problem = null;
        var saved = store.Update(found.Id, current =>
        {
            // Read again inside the save: it may have gone to Xero in the moment between.
            if (!Approvals.AssistantMayChange(current) || Approvals.AssistantMayChangeHere(current) == xeroDraft)
            {
                problem = $"That {Word(current)} has just moved on (gone to Xero, or been approved or emailed), so it wasn't changed.";
                return null;
            }
            // A draft in Xero: the change is kept here beside Xero's copy (BeforeChange), as when Eddie changes it, and
            // goes to Xero only when he clicks Update draft in Xero.
            if (xeroDraft && current.Xero.State != XeroWriteState.Editing)
                current = XeroEdits.StartChanging(current)!;
            var document = current.Document;
            if (lines is not null)
                document = document with { Lines = Lines(lines) };
            if (reference is not null)
                document = document with { Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim() };
            if (notes is not null)
                document = document with { Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim() };
            if (discountPercent is not null || discountAmount is not null)
                document = document with
                {
                    DocumentDiscountPercent = discountPercent is > 0 ? discountPercent : null,
                    DocumentDiscountAmount = discountAmount is > 0 and var off ? Money.Cents(off) : null,
                };
            // The totals are kept on the document, so they are worked out again from the lines and the discount.
            document = DocumentDefaults.Complete(document with { Subtotal = null, Total = null },
                store.GetSetting<DocumentSettings>("documents") ?? new DocumentSettings(), DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
            return xeroDraft
                ? current with { Document = document, Source = source, OffApprovals = null, ChangedByAssistant = clock.GetUtcNow() }
                : current with
                {
                    Document = document, Status = DocumentStatus.AwaitingApproval, Source = source, OffApprovals = null,
                    ChangedByAssistant = clock.GetUtcNow(),
                };
        });
        if (problem is not null || saved is null)
            throw new ArgumentException(problem ?? "It couldn't be changed. Use list_documents to see where it stands.");

        var problems = DocumentValidator.Validate(saved.Document);
        return new Prepared(saved.Id, Word(saved), saved.Document.Customer.Name, saved.Document.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            saved.Document.DueOrExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", Money.Rand(saved.Document.Total ?? 0), problems,
            (xeroDraft
                ? $"Changed in Webignite. Xero still has the old copy of draft {saved.Xero.Number}: it waits under To approve for Eddie to check, then he clicks Update draft in Xero (or Discard changes to keep Xero's). It is not sent to the customer."
                : "Changed in Webignite. It waits under To approve for Eddie to check, marked as changed. Nothing has gone to Xero, and it is not sent to the customer.")
            + (problems.Count > 0 ? " It still needs: " + string.Join(" ", problems) : ""));
    }

    private static string Word(StoredDocument s) => s.Document.Kind == DocumentKind.Quote ? "quote" : "invoice";

    private static List<Line> Lines(IReadOnlyList<DraftLine> lines) =>
        [.. lines.Where(l => !string.IsNullOrWhiteSpace(l.Description))
            .Select(l => new Line
            {
                Product = string.IsNullOrWhiteSpace(l.Item) ? null : l.Item.Trim(),
                Description = l.Description.Trim(),
                Quantity = l.UnitPrice is null && l.Quantity is null ? null : l.Quantity ?? 1,
                UnitPrice = l.UnitPrice,
            })];

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
        var wanted = Webignite.Core.Clients.ClientDirectory.Key(customer);
        return store.List(only)
            .Where(s => wanted.Length == 0 || Webignite.Core.Clients.ClientDirectory.Key(s.Document.Customer.Name).Contains(wanted, StringComparison.Ordinal))
            .Take(Math.Clamp(limit <= 0 ? 20 : limit, 1, 100))
            .Select(s => new Listed(s.Id, s.Document.Kind.ToString().ToLowerInvariant(), s.Xero.Number, s.Document.Customer.Name, s.Document.Reference,
                s.Document.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", Money.Rand(s.Document.Total ?? 0), StatusText(s),
                s.Document.Lines.Select(l => (string.IsNullOrWhiteSpace(l.Product) ? "" : l.Product + ": ")
                    + (l.IsPriced ? $"{l.Description}: {l.Quantity:0.##} x {Money.Rand(l.UnitPrice ?? 0)}" : l.Description)).ToList(),
                Approvals.AssistantMayChange(s), s.Document.Notes,
                s.Document.DocumentDiscountPercent is { } percent ? $"{percent:0.##}%" : s.Document.DocumentDiscountAmount is { } amount ? Money.Rand(amount) : null))
            .ToList();
    }

    private static string StatusText(StoredDocument s) => s switch
    {
        { Delivery.Emailed: not null } => "emailed to the customer",
        { Delivery.Approved: not null } => "approved in Xero, not emailed",
        { Xero.State: XeroWriteState.Verified } => "draft in Xero",
        { Xero.State: XeroWriteState.Editing, ChangedByAssistant: not null } => "draft in Xero, changed here and waiting for Eddie to update Xero",
        { Xero.State: XeroWriteState.Editing } => "draft in Xero, being changed by Eddie",
        { Status: DocumentStatus.AwaitingApproval } => "waiting for Eddie under To approve, not in Xero yet",
        { Xero.Id: not null } => "in Xero",
        _ => "draft on Eddie's PC, not in Xero yet",
    };
}
