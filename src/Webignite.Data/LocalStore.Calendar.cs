using Webignite.Core.Calendar;

namespace Webignite.Data;

/// <summary>Calendar events. The date is readable so a month is quick to find; the rest is encrypted.</summary>
public sealed partial class LocalStore
{
    private const string EventContext = "event:";

    public CalendarEvent SaveEvent(CalendarEvent calendarEvent)
    {
        Run("""
            INSERT INTO events (id, date, payload) VALUES ($id, $date, $payload)
            ON CONFLICT (id) DO UPDATE SET date = $date, payload = $payload
            """,
            ("$id", calendarEvent.Id), ("$date", Day(calendarEvent.Date)), ("$payload", Seal(EventContext + calendarEvent.Id, calendarEvent)));
        return calendarEvent;
    }

    public CalendarEvent? GetEvent(string id) => Find<CalendarEvent>("events", EventContext, id);

    public void DeleteEvent(string id) => Remove("events", id);

    /// <summary>Events from one date to another, both included.</summary>
    public IReadOnlyList<CalendarEvent> ListEvents(DateOnly from, DateOnly to) =>
        ReadAll<CalendarEvent>(EventContext, "SELECT id, payload FROM events WHERE date BETWEEN $from AND $to", ("$from", Day(from)), ("$to", Day(to)));

    private const string DiaryContext = "diary:";

    /// <summary>What was written on a day's page of the diary; empty when nothing was.</summary>
    public string DiaryNote(DateOnly day) => Find<DiaryNote>("diary_notes", DiaryContext, Day(day))?.Text ?? "";

    /// <summary>Keeps what was written on a day's page; an emptied page leaves nothing behind.</summary>
    public void SaveDiaryNote(DateOnly day, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            Remove("diary_notes", Day(day));
        else
            Put("diary_notes", DiaryContext, Day(day), new DiaryNote { Date = day, Text = text.TrimEnd() });
    }
}
