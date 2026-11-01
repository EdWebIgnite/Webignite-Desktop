using Webignite.Core.Documents;

namespace Webignite.Core.Tasks;

public sealed record TaskSettings
{
    /// <summary>Days after a quote goes to Xero before a follow-up task appears.</summary>
    public int QuoteFollowUpDays { get; init; } = 5;

    /// <summary>Days a draft can sit unfinished before a reminder appears.</summary>
    public int StaleDraftDays { get; init; } = 3;
}

/// <summary>
/// Works out the to-dos that quotes and invoices call for, so nobody has to remember them:
/// following up a quote, chasing an overdue invoice and finishing a forgotten draft.
/// </summary>
public static class AutomaticTasks
{
    public static IReadOnlyList<TaskItem> Suggest(IEnumerable<StoredDocument> documents, IReadOnlySet<string> knownKeys,
        TaskSettings settings, DateOnly today, DateTimeOffset now)
    {
        var tasks = new List<TaskItem>();
        foreach (var stored in documents)
        {
            var doc = stored.Document;
            var customer = string.IsNullOrWhiteSpace(doc.Customer.Name) ? null : doc.Customer.Name.Trim();
            var name = stored.XeroNumber ?? (doc.Kind == DocumentKind.Quote ? "quote" : "invoice");
            var created = DateOnly.FromDateTime(stored.Created.UtcDateTime);

            switch (stored.Status, doc.Kind)
            {
                case (DocumentStatus.InXero, _) when stored.Settled is not null:
                    break;

                case (DocumentStatus.InXero, DocumentKind.Quote):
                    var start = DateOnly.FromDateTime(stored.Updated.UtcDateTime);
                    var followUp = start.AddDays(settings.QuoteFollowUpDays);
                    if (doc.DueOrExpiryDate is not { } expiry || expiry >= today)
                        Add($"quote-follow-up:{stored.Id}", $"Follow up {name}{For(customer)}", followUp);
                    break;

                case (DocumentStatus.InXero, DocumentKind.Invoice) when Receivables.IsOverdue(stored, today):
                    Add($"invoice-overdue:{stored.Id}", $"Chase overdue {name}{For(customer)}, {Money.Rand(Receivables.Outstanding(stored))}", today);
                    break;

                case (DocumentStatus.Draft, _) when created.AddDays(settings.StaleDraftDays) <= today:
                    Add($"stale-draft:{stored.Id}", $"Finish the draft {(doc.Kind == DocumentKind.Quote ? "quote" : "invoice")}{For(customer)}", today);
                    break;
            }

            void Add(string key, string title, DateOnly due)
            {
                if (knownKeys.Contains(key))
                    return;
                tasks.Add(new TaskItem
                {
                    Id = TaskItem.NewId(),
                    Title = title,
                    Due = due,
                    Source = TaskSource.Auto,
                    DocumentId = stored.Id,
                    Customer = customer,
                    AutoKey = key,
                    Created = now,
                });
            }
        }
        return tasks;
    }

    private static string For(string? customer) => customer is null ? "" : $" for {customer}";
}
