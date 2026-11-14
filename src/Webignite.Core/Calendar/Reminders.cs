using System.Globalization;
using Webignite.Core.Projects;

namespace Webignite.Core.Calendar;

/// <summary>What a reminder is about, so a click on it can open the right thing.</summary>
public enum ReminderKind { Task, Event }

/// <summary>
/// Something with a time that is coming up: a task due at a time, or a calendar event that starts at one.
/// <paramref name="Key"/> is its own for that thing at that time, so it is said once, and said again if the
/// time is changed. <paramref name="At"/> is the time itself; <paramref name="From"/> when to start saying so.
/// </summary>
public sealed record Reminder(string Key, ReminderKind Kind, string TargetId, string Title, DateTime At, DateTime From, string Detail);

/// <summary>How the person wants to be reminded.</summary>
public sealed record ReminderSettings
{
    /// <summary>Whether tasks and events with a time are announced at all.</summary>
    public bool On { get; init; } = true;

    /// <summary>How long before the time the reminder shows.</summary>
    public int MinutesBefore { get; init; } = 10;

    /// <summary>Whether reminders, and the other notes that matter while away (new mail, a chat message, a site down, a payment), also show as a Windows notification when Webignite is behind other windows.</summary>
    public bool Windows { get; init; } = true;

    /// <summary>The reminders already shown, by key, with the time each was for; so none is shown twice, even after a restart.</summary>
    public IReadOnlyDictionary<string, DateTime> Shown { get; init; } = new Dictionary<string, DateTime>();
}

/// <summary>
/// Reminders for what has a time today: a task due at a time and a calendar event that starts at one are each
/// announced a little before, once. Things with only a day have no reminder; they are in Today and the diary.
/// </summary>
public static class Reminders
{
    /// <summary>The choices offered for how long before.</summary>
    public static IReadOnlyList<int> Leads { get; } = [0, 5, 10, 15, 30, 60];

    /// <summary>A reminder still counts for this long after its time, so one missed while the PC slept is shown on waking; older ones are let go.</summary>
    public static readonly TimeSpan StillWorthSaying = TimeSpan.FromHours(1);

    /// <summary>"At the time", "5 minutes before", "1 hour before".</summary>
    public static string LeadLabel(int minutes) => minutes switch
    {
        <= 0 => "At the time",
        60 => "1 hour before",
        _ => $"{minutes} minutes before",
    };

    /// <summary>
    /// Everything with a time from an hour ago to <paramref name="ahead"/> from now: open tasks given to the person
    /// that are due at a time, and events that start at one. Soonest first.
    /// </summary>
    public static IReadOnlyList<Reminder> Upcoming(IEnumerable<ProjectTask> mine, IReadOnlyDictionary<string, string> projectNames,
        IEnumerable<CalendarEvent> events, DateTime now, int minutesBefore, TimeSpan ahead)
    {
        var lead = TimeSpan.FromMinutes(Math.Max(0, minutesBefore));
        var (earliest, latest) = (now - StillWorthSaying, now + ahead + lead);
        var found = new List<Reminder>();
        foreach (var task in mine.Where(t => t.Deleted is null && !t.Done))
        {
            if (task.Due is not { } day || task.DueTime is not { } time)
                continue;
            var at = day.ToDateTime(time);
            if (at < earliest || at > latest)
                continue;
            found.Add(new Reminder($"task:{task.Id}:{at:yyyyMMddHHmm}", ReminderKind.Task, task.Id, task.Title, at, at - lead,
                projectNames.GetValueOrDefault(task.ProjectId) ?? ""));
        }
        foreach (var happening in events)
        {
            if (happening.Start is not { } time)
                continue;
            var at = happening.Date.ToDateTime(time);
            if (at < earliest || at > latest)
                continue;
            found.Add(new Reminder($"event:{happening.Id}:{at:yyyyMMddHHmm}", ReminderKind.Event, happening.Id, happening.Title, at, at - lead,
                happening.Location ?? ""));
        }
        return [.. found.OrderBy(r => r.At).ThenBy(r => r.Title, StringComparer.Ordinal)];
    }

    /// <summary>The reminders to show now: their time to be said has come, they aren't over an hour past, and they haven't been shown.</summary>
    public static IReadOnlyList<Reminder> Due(IEnumerable<Reminder> upcoming, DateTime now, IReadOnlyDictionary<string, DateTime> shown) =>
        [.. upcoming.Where(r => r.From <= now && now < r.At + StillWorthSaying && !shown.ContainsKey(r.Key))];

    /// <summary>The record of what was shown with these added, and anything for a time more than two days back dropped.</summary>
    public static IReadOnlyDictionary<string, DateTime> Remembered(IReadOnlyDictionary<string, DateTime> shown, IEnumerable<Reminder> justShown, DateTime now)
    {
        var kept = shown.Where(s => s.Value > now.AddDays(-2)).ToDictionary(s => s.Key, s => s.Value);
        foreach (var reminder in justShown)
            kept[reminder.Key] = reminder.At;
        return kept;
    }

    /// <summary>
    /// The reminder's second line: "Due at 14:00, in 10 minutes · Bosveld Builders new website", "Starts now · Teams",
    /// or "Was due at 09:00" for one that is past.
    /// </summary>
    public static string Text(Reminder reminder, DateTime now, IFormatProvider culture)
    {
        var time = reminder.At.ToString("t", culture);
        var minutes = (int)Math.Round((reminder.At - now).TotalMinutes);
        var (soon, past) = reminder.Kind == ReminderKind.Task ? ("Due", "Was due") : ("Starts", "Started");
        var when = minutes switch
        {
            < -1 => $"{past} at {time}",
            <= 1 => $"{soon} now",
            < 60 => $"{soon} at {time}, in {minutes} minutes",
            60 => $"{soon} at {time}, in an hour",
            _ => $"{soon} at {time}",
        };
        return reminder.Detail.Length > 0 ? $"{when} · {reminder.Detail}" : when;
    }
}
