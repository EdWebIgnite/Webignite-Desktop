using Webignite.Core.Documents;

namespace Webignite.Core.Xero;

/// <summary>A quote or sales invoice as Xero has it, for bringing in ones made in Xero itself.</summary>
public sealed record XeroSalesDocument
{
    public required string Id { get; init; }
    public required DocumentKind Kind { get; init; }
    public string? Number { get; init; }
    public required string ContactId { get; init; }
    public required string ContactName { get; init; }
    public DateOnly? Date { get; init; }
    public DateOnly? DueOrExpiry { get; init; }
    public string? Reference { get; init; }
    public string? Currency { get; init; }
    public IReadOnlyList<Line> Lines { get; init; } = [];
    public decimal? Subtotal { get; init; }
    public decimal? Tax { get; init; }
    public decimal? Total { get; init; }

    /// <summary>A quote's summary (its notes) and terms.</summary>
    public string? Summary { get; init; }
    public string? Terms { get; init; }

    public required string Status { get; init; }
    public decimal? AmountDue { get; init; }
    public decimal? AmountPaid { get; init; }
    public DateOnly? PaidOn { get; init; }

    /// <summary>An invoice Xero marked as sent to the customer.</summary>
    public bool SentToContact { get; init; }
}

/// <summary>What bringing in Xero's quotes and invoices found.</summary>
public sealed record XeroDocumentsPlan(IReadOnlyList<StoredDocument> ToAdd, IReadOnlyList<StoredDocument> ToRefresh);

/// <summary>
/// Quotes and invoices made in Xero itself, brought in so lists, Money, Reports and reminders see everything, not
/// only what Webignite made. They're kept as they are in Xero and changed there only; Webignite's own documents are
/// never touched here (the payment check keeps their status current).
/// </summary>
public static class XeroDocuments
{
    /// <summary>How far back the first look goes: invoices two years, quotes one. Anything still owed comes in whatever its age.</summary>
    public static readonly int InvoiceMonths = 24;
    public static readonly int QuoteMonths = 12;

    /// <summary>
    /// The new ones to add, and those brought in before that Xero has changed since. Deleted or voided ones are
    /// only brought in when they were here already, so old mistakes in Xero don't fill the lists.
    /// </summary>
    public static XeroDocumentsPlan Plan(IEnumerable<StoredDocument> existing, IEnumerable<XeroSalesDocument> fromXero, DateTimeOffset now)
    {
        var byXeroId = existing.Where(d => d.Xero.Id is not null)
            .GroupBy(d => d.Xero.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Webignite's own whose write to Xero broke before Xero's id came back: Check Xero links those, so a row
        // that may be one of them waits rather than coming in twice.
        var unsure = existing.Where(d => d.Xero.Id is null && d.Xero.State is XeroWriteState.Writing or XeroWriteState.Uncertain).ToList();
        var add = new List<StoredDocument>();
        var refresh = new List<StoredDocument>();
        foreach (var row in fromXero.DistinctBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (byXeroId.TryGetValue(row.Id, out var known))
            {
                if (known.Document.Origin == DocumentOrigin.Xero && ToStored(row, now, known) is var fresh && !Same(fresh, known))
                    refresh.Add(fresh);
                continue;
            }
            if (row.Status is XeroStatuses.Deleted or XeroStatuses.Voided || unsure.Any(d => MaybeSame(d, row)))
                continue;
            add.Add(ToStored(row, now, null));
        }
        return new XeroDocumentsPlan(add, refresh);
    }

    private static bool MaybeSame(StoredDocument ours, XeroSalesDocument row) =>
        ours.Document.Kind == row.Kind
        && (Same(ours.Xero.Number ?? ours.Document.Number, row.Number)
            || (Same(ours.Document.Customer.Name, row.ContactName) && ours.Document.Total == row.Total));

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    // Compared as JSON: the lines are lists, which records compare by reference.
    private static bool Same(StoredDocument a, StoredDocument b) =>
        System.Text.Json.JsonSerializer.Serialize(a with { Updated = b.Updated, Xero = a.Xero with { Checked = b.Xero.Checked } }, DocumentJson.Options)
        == System.Text.Json.JsonSerializer.Serialize(b, DocumentJson.Options);

    /// <summary>The document as kept here, from Xero's copy; one brought in before keeps its id, chasing and when it was added.</summary>
    public static StoredDocument ToStored(XeroSalesDocument row, DateTimeOffset now, StoredDocument? before)
    {
        var document = new Document
        {
            Kind = row.Kind,
            Origin = DocumentOrigin.Xero,
            Number = row.Number,
            Reference = string.IsNullOrWhiteSpace(row.Reference) ? null : row.Reference,
            Customer = new Customer { Name = row.ContactName },
            Date = row.Date,
            DueOrExpiryDate = row.DueOrExpiry,
            Currency = row.Currency,
            Lines = row.Lines,
            Subtotal = row.Subtotal,
            TaxTotal = row.Tax,
            Total = row.Total,
            Notes = string.IsNullOrWhiteSpace(row.Summary) ? null : row.Summary,
            Terms = string.IsNullOrWhiteSpace(row.Terms) ? null : row.Terms,
            OriginalStatus = row.Status,
            AmountPaid = row.AmountPaid,
            BalanceDue = row.AmountDue,
        };
        var draft = XeroStatuses.IsDraft(row.Status);
        var settled = draft ? null : Chasers.SettledBy(row.Kind, row.Status, row.AmountDue);
        var dated = row.Date is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : now;
        // Out to the customer: an approved invoice Xero marked sent, or a quote past draft.
        var wentOut = !draft && (row.Kind == DocumentKind.Quote ? row.Status != XeroStatuses.Draft : row.SentToContact);
        return new StoredDocument
        {
            Id = before?.Id ?? StoredDocument.NewId(),
            Document = document,
            Status = DocumentStatus.InXero,
            Source = DocumentSource.App,
            Xero = new XeroLink
            {
                Id = row.Id,
                Number = row.Number,
                State = XeroWriteState.Verified,
                ContactId = row.ContactId,
                Status = row.Status,
                AmountDue = row.AmountDue,
                AmountPaid = row.AmountPaid,
                PaidOn = row.PaidOn,
                Checked = now,
            },
            Delivery = (before?.Delivery ?? new Delivery()) with
            {
                Approved = before?.Delivery.Approved ?? (row.Kind == DocumentKind.Invoice && !draft ? dated : null),
                Emailed = before?.Delivery.Emailed ?? (wentOut ? dated : null),
            },
            Settled = settled is null ? null : before?.Settled is { } was ? was with { As = settled } : new SettledState { On = now, As = settled },
            Created = before?.Created ?? dated,
            Updated = now,
        };
    }
}
