using Webignite.Core.Renewals;
using Webignite.Core.Retainers;

namespace Webignite.Core.Calendar;

/// <summary>What the person wrote on a day's page of the diary.</summary>
public sealed record DiaryNote
{
    public required DateOnly Date { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>One ruled line of a diary page: an hour, and what is written in it.</summary>
public sealed record DiaryLine(TimeOnly Hour, IReadOnlyList<AgendaItem> Items);

/// <summary>A day's page: its hours down the page, and what has a day but no time.</summary>
public sealed record DiaryDay(DateOnly Date, IReadOnlyList<DiaryLine> Lines, IReadOnlyList<AgendaItem> Untimed);

/// <summary>
/// The diary: a day to a page, two pages open at a time. It writes nothing of its own besides the notes; the
/// events, to-dos, project tasks and dates on its pages are the same ones the calendar and the projects hold.
/// </summary>
public static class Diary
{
    /// <summary>The hours every page has, whether or not anything is in them.</summary>
    public const int FirstHour = 7;
    public const int LastHour = 18;

    /// <summary>
    /// The page for a day. It runs from seven to six, and earlier or later when something on the day does, so
    /// nothing with a time is ever left off the page.
    /// </summary>
    public static DiaryDay Day(DateOnly date, IEnumerable<AgendaItem> items)
    {
        var onDay = items.Where(i => i.Date == date).ToList();
        var timed = onDay.Where(i => i.Time is not null).OrderBy(i => i.Time).ThenBy(i => i.Kind).ToList();
        var first = Math.Min(FirstHour, timed.Count > 0 ? timed[0].Time!.Value.Hour : FirstHour);
        var last = Math.Max(LastHour, timed.Count > 0 ? timed[^1].Time!.Value.Hour : LastHour);
        var lines = new List<DiaryLine>();
        for (var hour = first; hour <= last; hour++)
            lines.Add(new DiaryLine(new TimeOnly(hour, 0), [.. timed.Where(i => i.Time!.Value.Hour == hour)]));
        return new DiaryDay(date, lines, [.. onDay.Where(i => i.Time is null).OrderBy(i => i.Done).ThenBy(i => i.Kind)]);
    }

    /// <summary>
    /// The two days open together. The left page is always the same for a given day, whichever of the pair was
    /// asked for, so turning pages forward and back lands on the same spreads, as in a bound book.
    /// </summary>
    public static (DateOnly Left, DateOnly Right) Spread(DateOnly day)
    {
        var left = day.DayNumber % 2 == 0 ? day : day.AddDays(-1);
        return (left, left.AddDays(1));
    }

    /// <summary>Renewals falling due and retainer invoices going out between two dates, as the diary notes them.</summary>
    public static IReadOnlyList<AgendaItem> MoneyDates(DateOnly from, DateOnly to, IEnumerable<Renewal> renewals, IEnumerable<Retainer> retainers)
    {
        var items = new List<AgendaItem>();
        foreach (var r in renewals.Where(r => r.RenewsOn >= from && r.RenewsOn <= to))
            items.Add(new AgendaItem(r.RenewsOn, AgendaKind.Renewal,
                string.IsNullOrWhiteSpace(r.ClientName) ? $"{r.Name} renews" : $"{r.Name} renews ({r.ClientName})", null, r.Id));
        foreach (var r in retainers.Where(r => !r.Paused))
            for (var month = Dates.MonthStart(from); month <= to; month = month.AddMonths(1))
            {
                var date = RetainerSchedule.DateIn(r, month);
                if (date >= from && date <= to && date >= r.Starts && (r.Ends is not { } ends || date <= ends))
                    items.Add(new AgendaItem(date, AgendaKind.Retainer, $"{r.Title} invoice goes out ({r.ClientName})", null, r.Id));
            }
        return items;
    }
}
