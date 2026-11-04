using System.Globalization;

namespace Webignite.Core.Calendar;

public static class CalendarEvents
{
    /// <summary>A time typed for an event: "9", "9:30", "14:00", "9h30" or "9.30". False when it isn't a time.</summary>
    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        var cleaned = (text ?? "").Trim().Replace('h', ':').Replace('.', ':');
        if (!cleaned.Contains(':') && int.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) && hour is >= 0 and < 24)
        {
            time = new TimeOnly(hour, 0);
            return true;
        }
        return TimeOnly.TryParseExact(cleaned, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
