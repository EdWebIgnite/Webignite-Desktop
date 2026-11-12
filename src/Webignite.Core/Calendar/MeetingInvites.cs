using System.Globalization;
using System.Text.RegularExpressions;

namespace Webignite.Core.Calendar;

/// <summary>A meeting read out of a mail: what a calendar invite says, or what could be made out from a link and the words around it.</summary>
public sealed record MeetingInvite
{
    public string Title { get; init; } = "";
    public DateOnly? Date { get; init; }
    public TimeOnly? Start { get; init; }
    public TimeOnly? End { get; init; }
    public string? Location { get; init; }
    public string? JoinLink { get; init; }

    /// <summary>The invite itself gave the day and time, so they can be trusted; otherwise they were guessed from the mail's words.</summary>
    public bool FromInvite { get; init; }
}

/// <summary>
/// Finds the meeting in a mail, so it can be added to the calendar with one click: from the calendar invite Outlook,
/// Teams, Google and Zoom attach (an .ics), or failing that from a Teams, Zoom, Meet or Webex link in the text.
/// Nothing is added by itself; the person checks what was read before it is saved.
/// </summary>
public static partial class MeetingInvites
{
    [GeneratedRegex(@"https://(?:teams\.microsoft\.com/(?:l/meetup-join|meet)/|teams\.live\.com/meet/|[\w.-]*zoom\.us/(?:j|my|w|s)/|meet\.google\.com/|[\w.-]*\.webex\.com/|(?:[\w-]+\.)?whereby\.com/|meet\.jit\.si/|meet\.goto\.com/|(?:global\.)?gotomeeting\.com/join/|gotomeet\.me/|meet\.zoho\.[a-z.]+/|[\w.-]*bluejeans\.com/|chime\.aws/|calendly\.com/events/)[^\s""'<>)\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();

    [GeneratedRegex(@"\b(?<h>[01]?\d|2[0-3])\s?[:h]\s?(?<m>[0-5]\d)\s*(?<ap>am|pm)?\b|\b(?<h>1[0-2]|0?[1-9])\s?(?<ap>am|pm)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Times();

    [GeneratedRegex(@"\b(?<d>[0-3]?\d)(?:st|nd|rd|th)?\s+(?:of\s+)?(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?,?(?:\s+(?<y>20\d\d))?|\b(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(?<d>[0-3]?\d)(?:st|nd|rd|th)?(?:,?\s+(?<y>20\d\d))?|\b(?<y>20\d\d)-(?<mn>[01]\d)-(?<d>[0-3]\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DatesIn();

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>A link typed or pasted by the person for a meeting, tidied: any web address will do, since they chose it. Null when it isn't one.</summary>
    public static string? TypedLink(string? typed)
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0)
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "https" or "http" && url.Host.Contains('.') ? url.AbsoluteUri : null;
    }

    /// <summary>The first link to an online meeting in some text or HTML, or null.</summary>
    public static string? JoinLink(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var found = Links().Match(text.Replace("&amp;", "&", StringComparison.Ordinal));
        return found.Success ? found.Value.TrimEnd('.', ',', ';') : null;
    }

    /// <summary>
    /// The meeting in a mail. A calendar invite wins, as it states the day and time; without one, a meeting link in
    /// the text gives a meeting whose day and time are a best guess from the words, to be checked. Null when the
    /// mail has neither.
    /// </summary>
    public static MeetingInvite? InMail(string subject, string text, string? html, string? calendar, DateOnly received, TimeZoneInfo zone)
    {
        var link = JoinLink(html) ?? JoinLink(text);
        if (FromCalendar(calendar, zone) is { } invite)
            return invite with
            {
                Title = invite.Title.Length > 0 ? invite.Title : CleanSubject(subject),
                JoinLink = invite.JoinLink ?? link,
            };
        if (link is null)
            return null;
        var date = DateIn(text, received);
        var times = Times().Matches(text).Select(TimeOf).OfType<TimeOnly>().Take(2).ToList();
        return new MeetingInvite
        {
            Title = CleanSubject(subject),
            Date = date,
            Start = times.Count > 0 ? times[0] : null,
            End = times.Count > 1 && times[1] > times[0] ? times[1] : null,
            JoinLink = link,
        };
    }

    private static string CleanSubject(string subject)
    {
        var s = subject.Trim();
        foreach (var prefix in new[] { "Re:", "Fw:", "Fwd:", "Invitation:", "Updated invitation:", "Accepted:" })
            while (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                s = s[prefix.Length..].TrimStart();
        return s.Length == 0 ? "Meeting" : s;
    }

    private static TimeOnly? TimeOf(Match m)
    {
        var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Groups["ap"].Success)
        {
            var pm = m.Groups["ap"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
            if (hour > 12)
                return null;
            hour = hour % 12 + (pm ? 12 : 0);
        }
        return hour < 24 ? new TimeOnly(hour, minute) : null;
    }

    /// <summary>The first day named in the text, on or after the day the mail came: "14 November", "Nov 14, 2026", "2026-11-14", "tomorrow".</summary>
    public static DateOnly? DateIn(string text, DateOnly received)
    {
        var m = DatesIn().Match(text);
        if (m.Success)
        {
            var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            var month = m.Groups["mn"].Success
                ? int.Parse(m.Groups["mn"].Value, CultureInfo.InvariantCulture)
                : Array.IndexOf(Months, m.Groups["mon"].Value.ToLowerInvariant()) + 1;
            var year = m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture) : received.Year;
            if (month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
            {
                var date = new DateOnly(year, month, day);
                // With no year said, a day already gone means next year's.
                if (!m.Groups["y"].Success && date < received && day <= DateTime.DaysInMonth(year + 1, month))
                    date = new DateOnly(year + 1, month, day);
                return date;
            }
        }
        if (Regex.IsMatch(text, @"\btomorrow\b", RegexOptions.IgnoreCase))
            return received.AddDays(1);
        if (Regex.IsMatch(text, @"\btoday\b", RegexOptions.IgnoreCase))
            return received;
        return null;
    }

    /// <summary>The first event in a calendar file (.ics), with its times in the PC's own time zone. Null when there is none.</summary>
    public static MeetingInvite? FromCalendar(string? calendar, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(calendar))
            return null;
        // A long line is carried over onto the next, which then starts with a space or tab.
        var unfolded = Regex.Replace(calendar.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), @"\n[ \t]", "");
        var inEvent = false;
        var fields = new Dictionary<string, (string Params, string Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in unfolded.Split('\n'))
        {
            if (line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                inEvent = true;
            else if (line.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase))
                break;
            else if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase) && inEvent)
                inEvent = line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) || !line.StartsWith("BEGIN:VALARM", StringComparison.OrdinalIgnoreCase);
            else if (inEvent && line.IndexOf(':') is > 0 and var colon)
            {
                var name = line[..colon];
                var semi = name.IndexOf(';');
                fields.TryAdd(semi < 0 ? name : name[..semi], (semi < 0 ? "" : name[(semi + 1)..], line[(colon + 1)..]));
            }
        }
        if (!fields.TryGetValue("DTSTART", out var starts) || When(starts.Params, starts.Value, zone) is not { } start)
            return null;
        var end = fields.TryGetValue("DTEND", out var ends) ? When(ends.Params, ends.Value, zone) : null;
        string? Text(string name) => fields.TryGetValue(name, out var f) && Unescape(f.Value) is { Length: > 0 } t ? t : null;
        var location = Text("LOCATION");
        var link = JoinLink(Text("X-MICROSOFT-SKYPETEAMSMEETINGURL")) ?? JoinLink(Text("URL")) ?? JoinLink(location)
            ?? JoinLink(Text("DESCRIPTION")) ?? JoinLink(Text("X-ALT-DESC")) ?? JoinLink(Text("X-GOOGLE-CONFERENCE"));
        return new MeetingInvite
        {
            Title = Text("SUMMARY") ?? "",
            Date = DateOnly.FromDateTime(start.At),
            Start = start.AllDay ? null : TimeOnly.FromDateTime(start.At),
            End = end is { AllDay: false } e && e.At.Date == start.At.Date ? TimeOnly.FromDateTime(e.At) : null,
            // A location that is only the meeting's link says nothing the Join button doesn't.
            Location = location is not null && JoinLink(location) is { } l && l.Length >= location.Trim().Length - 2 ? null : location,
            JoinLink = link,
            FromInvite = true,
        };
    }

    private static string Unescape(string value) => value.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",", StringComparison.Ordinal).Replace(@"\;", ";", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal).Trim();

    // A calendar file's date: all day (20261114), UTC (…T090000Z), in a named zone (TZID=…) or as it stands.
    private static (DateTime At, bool AllDay)? When(string parameters, string value, TimeZoneInfo zone)
    {
        value = value.Trim();
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return (day, true);
        var utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            return null;
        if (utc)
            return (TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), zone), false);
        var named = parameters.Split(';').FirstOrDefault(p => p.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))?[5..].Trim('"');
        if (named is not null)
        {
            try
            {
                var from = TimeZoneInfo.FindSystemTimeZoneById(named);
                return (TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(at, DateTimeKind.Unspecified), from, zone), false);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                // A zone this PC doesn't know: the time is shown as written, which is right when sender and reader share a zone.
            }
        }
        return (at, false);
    }
}
