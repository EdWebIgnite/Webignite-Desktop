using Webignite.Core.Documents;

namespace Webignite.Core.Tasks;

public sealed record TaskSettings
{
    /// <summary>Days a draft can sit unfinished before a reminder appears.</summary>
    public int StaleDraftDays { get; init; } = 3;
}

/// <summary>
/// Works out the to-dos that quotes and invoices call for, so nobody has to remember them: following up a quote,
/// chasing an overdue invoice and finishing a forgotten draft. When to chase is decided by <see cref="Chasers"/>
/// alone (a week after a quote was emailed, an invoice once overdue and then weekly), so the to-dos and the
/// morning summary always agree.
/// </summary>
public static class AutomaticTasks
{
    public static IReadOnlyList<TaskItem> Suggest(IEnumerable<StoredDocument> documents, IReadOnlySet<string> knownKeys,
        TaskSettings settings, DateOnly today, DateTimeOffset now)
    {
        var all = documents.ToList();
        var tasks = new List<TaskItem>();
        // One to-do per chase that is due. A reminder round counts up each time one is emailed, so the next
        // week's reminder is a new to-do, and the one before it was ticked off when the mail went.
        foreach (var chase in Chasers.Find(all, today).Where(c => c.Due))
        {
            var stored = chase.Stored;
            var customer = CustomerOf(stored);
            if (chase.Kind == ChaseKind.FollowUp)
                Add(stored, customer, $"{FollowUpKey}{stored.Id}", $"Follow up {chase.Number}{For(customer)}", today);
            else
                Add(stored, customer, $"{ReminderKey}{stored.Id}:{stored.ChaseCount}",
                    $"Chase overdue {chase.Number}{For(customer)}, {Money.Rand(Receivables.Outstanding(stored))}", today);
        }
        foreach (var stored in all)
        {
            var doc = stored.Document;
            var created = DateOnly.FromDateTime(stored.Created.UtcDateTime);
            if (stored.Status == DocumentStatus.Draft && created.AddDays(settings.StaleDraftDays) <= today)
                Add(stored, CustomerOf(stored), $"stale-draft:{stored.Id}",
                    $"Finish the draft {(doc.Kind == DocumentKind.Quote ? "quote" : "invoice")}{For(CustomerOf(stored))}", today);
        }
        return tasks;

        void Add(StoredDocument stored, string? customer, string key, string title, DateOnly due)
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

    public const string FollowUpKey = "quote-follow-up:";
    public const string ReminderKey = "invoice-overdue:";

    /// <summary>Whether a to-do is an automatic follow-up or reminder, ticked off when the mail goes or it's settled.</summary>
    public static bool IsChase(TaskItem task) => !task.Done && task.AutoKey is { } key
        && (key.StartsWith(FollowUpKey, StringComparison.Ordinal) || key.StartsWith(ReminderKey, StringComparison.Ordinal));

    private static string? CustomerOf(StoredDocument stored) =>
        string.IsNullOrWhiteSpace(stored.Document.Customer.Name) ? null : stored.Document.Customer.Name.Trim();

    private static string For(string? customer) => customer is null ? "" : $" for {customer}";
}
