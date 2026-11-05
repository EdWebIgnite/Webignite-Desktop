using System.Globalization;
using Webignite.Core.Calendar;
using Webignite.Core.Tasks;

namespace Webignite.Core.Tools;

/// <summary>What the quick box saves what was typed as.</summary>
public enum CaptureKind { Note, Todo, Event }

/// <summary>When a to-do from the quick box is due.</summary>
public enum CaptureDue { NoDay, Today, Tomorrow, NextWeek }

/// <summary>
/// The quick box from any program (Ctrl+Alt+N): what is typed becomes a note, a to-do or a calendar event.
/// For a to-do or an event the first line is the title and the rest is kept as its notes.
/// </summary>
public static class QuickCapture
{
    public static readonly IReadOnlyList<CaptureDue> DueChoices = [CaptureDue.NoDay, CaptureDue.Today, CaptureDue.Tomorrow, CaptureDue.NextWeek];

    public static string Title(CaptureKind kind) => kind switch
    {
        CaptureKind.Todo => "Quick to-do",
        CaptureKind.Event => "Quick event",
        _ => "Quick note",
    };

    public static string Hint(CaptureKind kind) => kind switch
    {
        CaptureKind.Todo => "What needs doing. Any lines after the first are kept as its notes.",
        CaptureKind.Event => "What it is, e.g. Site walk-through with Pieter. Any lines after the first are kept as notes.",
        _ => "Type it down. With more than one line, the first is the title. Start a line with [ ] for a checklist.",
    };

    public static string DueLabel(CaptureDue due) => due switch
    {
        CaptureDue.Today => "Today",
        CaptureDue.Tomorrow => "Tomorrow",
        CaptureDue.NextWeek => "Next week",
        _ => "No day",
    };

    /// <summary>The day a to-do is due. Next week is the coming Monday.</summary>
    public static DateOnly? Due(CaptureDue due, DateOnly today) => due switch
    {
        CaptureDue.Today => today,
        CaptureDue.Tomorrow => today.AddDays(1),
        CaptureDue.NextWeek => today.AddDays(DaysToMonday(today)),
        _ => null,
    };

    // On a Monday, next week is the Monday after.
    private static int DaysToMonday(DateOnly today) => ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 is var days and > 0 ? days : 7;

    /// <summary>The first line as the title and the rest as notes, both trimmed. Null notes when there are none.</summary>
    public static (string Title, string? Notes) Split(string typed)
    {
        var lines = (typed ?? "").Replace("\r", "", StringComparison.Ordinal).Trim('\n', ' ').Split('\n');
        var title = lines[0].Trim();
        var notes = string.Join('\n', lines.Skip(1)).Trim();
        return (title, notes.Length == 0 ? null : notes);
    }

    public static TaskItem? Todo(string typed, CaptureDue due, DateOnly today, string id, DateTimeOffset now)
    {
        var (title, notes) = Split(typed);
        return title.Length == 0 ? null : new TaskItem { Id = id, Title = title, Notes = notes, Due = Due(due, today), Created = now };
    }

    /// <summary>
    /// An event on the day picked, at the time typed: blank for all day, "9:30", "14:00", "2pm", or a range like
    /// "9:00-10:30" or "2pm to 3pm". Returns why not when the title or time can't be used.
    /// </summary>
    public static (CalendarEvent? Event, string? Problem) Event(string typed, DateOnly? date, string? time, string id)
    {
        var (title, notes) = Split(typed);
        if (title.Length == 0)
            return (null, "Type what the event is.");
        if (date is not { } day)
            return (null, "Pick the day it's on.");
        var (start, end, problem) = Times(time);
        if (problem is not null)
            return (null, problem);
        return (new CalendarEvent { Id = id, Title = title, Notes = notes, Date = day, Start = start, End = end }, null);
    }

    /// <summary>The start and end typed, or nothing for all day.</summary>
    public static (TimeOnly? Start, TimeOnly? End, string? Problem) Times(string? text)
    {
        var typed = (text ?? "").Trim();
        if (typed.Length == 0)
            return (null, null, null);
        var parts = typed.Split(["-", "–", " to "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        const string Bad = "The time doesn't look right. Type it like 9:30, 14:00 or 9:00-10:30, or leave it empty for all day.";
        if (parts.Length is 0 or > 2 || Time(parts[0]) is not { } start)
            return (null, null, Bad);
        if (parts.Length == 1)
            return (start, null, null);
        if (Time(parts[1]) is not { } end)
            return (null, null, Bad);
        return end <= start ? (null, null, "The end is before the start. Type it like 9:00-10:30.") : (start, end, null);
    }

    /// <summary>"9", "9:30", "09h30", "14:00", "2pm" or "2:30 pm".</summary>
    public static TimeOnly? Time(string text)
    {
        var t = text.Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal).Replace('h', ':').Replace('.', ':');
        var pm = t.EndsWith("pm", StringComparison.Ordinal);
        var am = t.EndsWith("am", StringComparison.Ordinal);
        if (pm || am)
            t = t[..^2];
        var bits = t.Split(':');
        if (bits.Length > 2 || !int.TryParse(bits[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
            return null;
        var minute = 0;
        if (bits.Length == 2 && (bits[1].Length != 2 || !int.TryParse(bits[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute)))
            return null;
        if (pm || am)
        {
            if (hour is < 1 or > 12)
                return null;
            hour = hour % 12 + (pm ? 12 : 0);
        }
        return hour is >= 0 and < 24 && minute is >= 0 and < 60 ? new TimeOnly(hour, minute) : null;
    }
}
