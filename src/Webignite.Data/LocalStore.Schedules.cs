using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Renewals;
using Webignite.Core.Retainers;
using Webignite.Core.Tasks;

namespace Webignite.Data;

/// <summary>
/// What the calendar calls for, applied: automatic to-dos, monthly costs, retainer and renewal drafts. The rules
/// (what is due, and when) are in Core; this saves what they say, each item exactly once.
/// </summary>
public sealed partial class LocalStore
{
    /// <summary>Adds the reminders that quotes and invoices call for. Returns how many were added.</summary>
    public int RaiseAutomaticTasks(TaskSettings settings, DateOnly today)
    {
        var added = AutomaticTasks.Suggest(List(), AutoTaskKeys(), settings, today, clock.GetUtcNow());
        foreach (var task in added)
            SaveTask(task);
        return added.Count;
    }

    /// <summary>Adds the expenses monthly items call for up to today. Returns how many were added.</summary>
    public int AddRecurringExpenses(DateOnly today)
    {
        var due = RecurringExpenses.Due(ListRecurring(), RecurringKeys(), today, clock.GetUtcNow());
        foreach (var entry in due)
            SaveEntry(entry);
        return due.Count;
    }

    /// <summary>Every invoice the app has drafted by itself (retainer months, renewals), by key, with the draft it made.</summary>
    public IReadOnlyDictionary<string, string> DraftsMade()
    {
        using var command = Command("SELECT key, document_id FROM drafts_made");
        using var reader = command.ExecuteReader();
        var runs = new Dictionary<string, string>();
        while (reader.Read())
            runs[reader.GetString(0)] = reader.GetString(1);
        return runs;
    }

    /// <summary>
    /// Saves a draft the app made by itself, with its key, in one go: both land or neither does,
    /// so a deleted draft is never made again and a crash never makes two.
    /// </summary>
    private StoredDocument SaveAutomaticDraft(string key, Document invoice) => InOneGo(() =>
    {
        var stored = Save(new StoredDocument { Id = StoredDocument.NewId(), Document = invoice });
        Run("INSERT INTO drafts_made (key, document_id) VALUES ($key, $id)", ("$key", key), ("$id", stored.Id));
        return stored;
    });

    /// <summary>
    /// Drafts the invoices retainers call for up to today. They are drafts on this PC only;
    /// nothing goes to Xero until a person sends it. Returns how many were drafted.
    /// </summary>
    public int DraftRetainerInvoices(DocumentSettings settings, DateOnly today)
    {
        var due = RetainerSchedule.Due(ListRetainers(), DraftsMade().Keys.ToHashSet(), settings, today);
        foreach (var (key, invoice) in due)
            SaveAutomaticDraft(key, invoice);
        return due.Count;
    }

    /// <summary>
    /// Adds renewal reminders to the to-do list and drafts invoices for renewals clients pay for,
    /// once each is inside its reminder window. Returns how many of each were added.
    /// </summary>
    public (int Reminders, int Invoices) RaiseRenewals(DocumentSettings settings, DateOnly today)
    {
        var renewals = ListRenewals();
        var reminders = RenewalRules.Reminders(renewals, AutoTaskKeys(), today, clock.GetUtcNow());
        foreach (var task in reminders)
            SaveTask(task);
        var invoices = RenewalRules.InvoicesDue(renewals, DraftsMade().Keys.ToHashSet(), settings, today);
        foreach (var (key, invoice) in invoices)
            SaveAutomaticDraft(key, invoice);
        return (reminders.Count, invoices.Count);
    }

    /// <summary>
    /// Marks a renewal done: moves it to the next date and adds what it cost under Money, in one go, so a crash
    /// in between can't add the cost without moving the date (and add it again next time).
    /// </summary>
    public Renewal MarkRenewed(Renewal renewal, DateOnly paidOn) => InOneGo(() =>
    {
        var (next, expense) = RenewalRules.Renewed(renewal, paidOn, clock.GetUtcNow());
        if (expense is not null)
            SaveEntry(expense);
        return SaveRenewal(next);
    });
}
