using Webignite.Core.Finance;

namespace Webignite.Data;

/// <summary>Money in and out, and the monthly costs that add themselves. Kind and date are readable for lists.</summary>
public sealed partial class LocalStore
{
    private const string EntryContext = "entry:";
    private const string RecurringContext = "recurring:";

    public MoneyEntry SaveEntry(MoneyEntry entry)
    {
        Run("""
            INSERT INTO entries (id, kind, date, recurring_key, payload) VALUES ($id, $kind, $date, $key, $payload)
            ON CONFLICT (id) DO UPDATE SET kind = $kind, date = $date, payload = $payload, deleted = 0
            """,
            ("$id", entry.Id), ("$kind", entry.Kind.ToString()), ("$date", Day(entry.Date)), ("$key", entry.RecurringKey),
            ("$payload", Seal(EntryContext + entry.Id, entry)));
        return entry;
    }

    /// <summary>Removes an entry. One added by a monthly expense keeps a marker so it isn't added again.</summary>
    public void DeleteEntry(string id) => Run("""
        UPDATE entries SET deleted = 1, payload = x'' WHERE id = $id AND recurring_key IS NOT NULL;
        DELETE FROM entries WHERE id = $id AND recurring_key IS NULL;
        """, ("$id", id));

    /// <summary>Entries dated from one day to another, both included, oldest first.</summary>
    public IReadOnlyList<MoneyEntry> ListEntries(DateOnly from, DateOnly to) =>
        ReadAll<MoneyEntry>(EntryContext, "SELECT id, payload FROM entries WHERE deleted = 0 AND date >= $from AND date <= $to ORDER BY date, id",
            ("$from", Day(from)), ("$to", Day(to)));

    /// <summary>Every monthly-cost key ever added, including deleted entries.</summary>
    public IReadOnlySet<string> RecurringKeys() => ReadKeys("SELECT recurring_key FROM entries WHERE recurring_key IS NOT NULL");

    public RecurringExpense SaveRecurring(RecurringExpense recurring) => Put("recurring", RecurringContext, recurring.Id, recurring);

    /// <summary>Stops a monthly expense. Entries it already added stay, as that money was spent.</summary>
    public void DeleteRecurring(string id) => Remove("recurring", id);

    public IReadOnlyList<RecurringExpense> ListRecurring() =>
        ReadAll<RecurringExpense>(RecurringContext, "SELECT id, payload FROM recurring").OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
