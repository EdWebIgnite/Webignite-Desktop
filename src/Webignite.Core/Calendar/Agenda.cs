using Webignite.Core.Documents;
using Webignite.Core.Tasks;

namespace Webignite.Core.Calendar;

public enum AgendaKind
{
    Event,
    Task,
    QuoteExpires,
    InvoiceDue,
}

/// <summary>One thing on a day: an event, a task, or a date from a quote or invoice.</summary>
public sealed record AgendaItem(DateOnly Date, AgendaKind Kind, string Title, TimeOnly? Time, string SourceId, bool Done = false)
{
    /// <summary>Timed items first in time order, then all-day items.</summary>
    public (int, TimeOnly, AgendaKind) SortKey => (Time is null ? 1 : 0, Time ?? TimeOnly.MinValue, Kind);
}

/// <summary>Brings events, tasks and document dates together for the calendar and planner.</summary>
public static class Agenda
{
    public static IReadOnlyList<AgendaItem> Build(DateOnly from, DateOnly to, IEnumerable<CalendarEvent> events,
        IEnumerable<TaskItem> tasks, IEnumerable<StoredDocument> documents)
    {
        var items = new List<AgendaItem>();
        bool InRange(DateOnly date) => date >= from && date <= to;

        foreach (var e in events.Where(e => InRange(e.Date)))
            items.Add(new AgendaItem(e.Date, AgendaKind.Event, e.Title, e.Start, e.Id));

        foreach (var t in tasks.Where(t => t.Due is { } due && InRange(due)))
            items.Add(new AgendaItem(t.Due!.Value, AgendaKind.Task, t.Title, null, t.Id, t.Done));

        // Only documents that went to Xero carry real dates; drafts would just clutter the calendar.
        foreach (var d in documents.Where(d => d.Status == DocumentStatus.InXero && d.Document.DueOrExpiryDate is { } date && InRange(date)))
        {
            var doc = d.Document;
            var name = d.XeroNumber ?? (doc.Kind == DocumentKind.Quote ? "Quote" : "Invoice");
            var customer = string.IsNullOrWhiteSpace(doc.Customer.Name) ? "" : $" ({doc.Customer.Name})";
            items.Add(doc.Kind == DocumentKind.Quote
                ? new AgendaItem(doc.DueOrExpiryDate!.Value, AgendaKind.QuoteExpires, $"{name} expires{customer}", null, d.Id)
                : new AgendaItem(doc.DueOrExpiryDate!.Value, AgendaKind.InvoiceDue, $"{name} due{customer}, {Money.Rand(doc.Total ?? 0)}", null, d.Id));
        }

        return items.OrderBy(i => i.Date).ThenBy(i => i.SortKey).ToList();
    }

    /// <summary>The Monday-to-Sunday weeks that cover a month, as the month grid shows them.</summary>
    public static (DateOnly First, DateOnly Last) MonthGrid(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var lastOfMonth = first.AddMonths(1).AddDays(-1);
        var end = lastOfMonth.AddDays((7 - (int)lastOfMonth.DayOfWeek) % 7);
        return (start, end);
    }

    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
