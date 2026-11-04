namespace Webignite.Core;

/// <summary>
/// The schedule shared by everything that comes round once a month (retainer invoices and monthly costs):
/// which day in a month, which months are due, pausing and resuming, and whether it's still running.
/// </summary>
public static class MonthlySchedule
{
    /// <summary>The day it falls on in a month. Day 31 lands on the 30th in November, the 28th or 29th in February.</summary>
    public static DateOnly DateIn(int dayOfMonth, DateOnly month) =>
        new(month.Year, month.Month, Math.Min(Math.Clamp(dayOfMonth, 1, 31), DateTime.DaysInMonth(month.Year, month.Month)));

    /// <summary>The first of each month from the start month up to today's month, or the end month when that's earlier.</summary>
    public static IEnumerable<DateOnly> MonthsSoFar(DateOnly starts, DateOnly? ends, DateOnly today)
    {
        var last = Dates.MonthStart(ends is { } end && end < today ? end : today);
        for (var month = Dates.MonthStart(starts); month <= last; month = month.AddMonths(1))
            yield return month;
    }

    /// <summary>Resuming starts again from this month, so the months it was paused aren't filled in afterwards.</summary>
    public static DateOnly ResumeFrom(DateOnly starts, DateOnly today) => Dates.MonthStart(today) > starts ? Dates.MonthStart(today) : starts;

    /// <summary>Running this month: not paused and not ended before this month.</summary>
    public static bool IsRunning(bool paused, DateOnly? ends, DateOnly today) =>
        !paused && (ends is null || Dates.MonthStart(ends.Value) >= Dates.MonthStart(today));

    /// <summary>An end month before the start month, which can't be saved.</summary>
    public static bool EndsBeforeStart(DateOnly starts, DateOnly? ends) => ends is { } end && Dates.MonthStart(end) < Dates.MonthStart(starts);
}
