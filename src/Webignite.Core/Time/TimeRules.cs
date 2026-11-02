using System.Globalization;
using System.Text.RegularExpressions;
using Webignite.Core.Clients;

namespace Webignite.Core.Time;

/// <summary>One line of a time summary: who or what, and how long.</summary>
public sealed record TimeTotal(string Label, int Minutes, string? ClientId, string? ProjectId);

/// <summary>Reading typed times, showing them, stopping the timer and adding up a week or month.</summary>
public static partial class TimeRules
{
    /// <summary>Longest single entry: anything more is surely a typo or a timer left running overnight.</summary>
    public const int MaxMinutes = 16 * 60;

    public const string NoClient = "Not for a client";

    /// <summary>
    /// Reads "1.5", "1,5", "1:30", "90m", "90 min", "1h30", "1 h 30 min" or "2h" as minutes.
    /// A plain number is hours. Returns null for anything else, zero, or more than <see cref="MaxMinutes"/>.
    /// </summary>
    public static int? ParseMinutes(string text)
    {
        var plain = text.Trim().ToLowerInvariant().Replace(" ", "");
        if (plain.Length == 0)
            return null;
        int? minutes = null;
        if (decimal.TryParse(plain.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var hours))
            minutes = (int)Math.Round(hours * 60);
        else if (Clock().Match(plain) is { Success: true } clock)
            minutes = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
        else if (HoursAndMinutes().Match(plain) is { Success: true } hm && (hm.Groups[1].Success || hm.Groups[2].Success))
            minutes = (int)Math.Round(Number(hm.Groups[1].Value) * 60 + Number(hm.Groups[2].Value));
        return minutes is > 0 and <= MaxMinutes ? minutes : null;
    }

    private static decimal Number(string text) =>
        text.Length == 0 ? 0 : decimal.Parse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d{1,2}):([0-5]\d)$")]
    private static partial Regex Clock();

    [GeneratedRegex(@"^(?:(\d+(?:[.,]\d+)?)(?:h|hr|hrs|hour|hours))?(?:(\d+)(?:m|min|mins|minutes)?)?$")]
    private static partial Regex HoursAndMinutes();

    /// <summary>"45 min", "1 h", "2 h 15 min".</summary>
    public static string Format(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    /// <summary>"1:05:09" for the running timer.</summary>
    public static string Clock(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

    /// <summary>
    /// Turns a stopped timer into an entry, dated the day it started. Rounds to the nearest minute, at least one.
    /// Returns null if it ran for less than 30 seconds (started by mistake).
    /// </summary>
    public static TimeEntry? Stop(RunningTimer timer, DateTimeOffset now)
    {
        var elapsed = now - timer.Started;
        if (elapsed < TimeSpan.FromSeconds(30))
            return null;
        var minutes = Math.Clamp((int)Math.Round(elapsed.TotalMinutes), 1, MaxMinutes);
        return new TimeEntry
        {
            Id = TimeEntry.NewId(),
            Date = DateOnly.FromDateTime(timer.Started.ToLocalTime().DateTime),
            Minutes = minutes,
            Note = timer.Note.Trim(),
            ClientId = timer.ClientId,
            ProjectId = timer.ClientId is null ? null : timer.ProjectId,
            Created = now,
        };
    }

    /// <summary>Monday of the week a date falls in.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Totals per client, and per project under each client, biggest first. Time not for a client is one line at the end.
    /// Labels read "Karoo Coffee Roasters" or "Karoo Coffee Roasters: New website".
    /// </summary>
    public static IReadOnlyList<TimeTotal> Totals(IEnumerable<TimeEntry> entries, IReadOnlyList<Client> clients)
    {
        var byId = clients.ToDictionary(c => c.Id);
        var lines = new List<TimeTotal>();
        var list = entries.ToList();
        foreach (var client in list.Where(e => e.ClientId is not null).GroupBy(e => e.ClientId!).OrderByDescending(g => g.Sum(e => e.Minutes)))
        {
            var known = byId.GetValueOrDefault(client.Key);
            var name = known?.Name ?? "A removed client";
            lines.Add(new TimeTotal(name, client.Sum(e => e.Minutes), client.Key, null));
            foreach (var project in client.Where(e => e.ProjectId is not null).GroupBy(e => e.ProjectId!).OrderByDescending(g => g.Sum(e => e.Minutes)))
            {
                var projectName = known?.Projects.FirstOrDefault(p => p.Id == project.Key)?.Name ?? "A removed project";
                lines.Add(new TimeTotal($"{name}: {projectName}", project.Sum(e => e.Minutes), client.Key, project.Key));
            }
        }
        var loose = list.Where(e => e.ClientId is null).Sum(e => e.Minutes);
        if (loose > 0)
            lines.Add(new TimeTotal(NoClient, loose, null, null));
        return lines;
    }

    /// <summary>"Karoo Coffee Roasters", "Karoo Coffee Roasters: New website" or "Not for a client".</summary>
    public static string Describe(TimeEntry entry, IReadOnlyList<Client> clients)
    {
        if (entry.ClientId is null)
            return NoClient;
        var client = clients.FirstOrDefault(c => c.Id == entry.ClientId);
        var name = client?.Name ?? "A removed client";
        var project = entry.ProjectId is null ? null : client?.Projects.FirstOrDefault(p => p.Id == entry.ProjectId)?.Name;
        return project is null ? name : $"{name}: {project}";
    }
}
