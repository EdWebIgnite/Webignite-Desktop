using System.Globalization;

namespace Webignite.Core.Tools;

/// <summary>Rules for screen recordings: the size the video can be, how sharp, how it is trimmed and named.</summary>
public static class Recordings
{
    public const int FramesPerSecond = 20;

    /// <summary>A trimmed recording is never shorter than this.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(1);

    /// <summary>
    /// H.264 video needs an even width and height, and at least a little of each; the odd pixel is left off.
    /// Null when the area is too small to record.
    /// </summary>
    public static ScreenGrab.Area? VideoArea(ScreenGrab.Area area)
    {
        var even = area with { Width = area.Width & ~1, Height = area.Height & ~1 };
        return even.Width < 32 || even.Height < 32 ? null : even;
    }

    /// <summary>Bits a second for the video: enough for sharp text, not so much the file balloons.</summary>
    public static int Bitrate(int width, int height, int framesPerSecond) =>
        (int)Math.Clamp((long)width * height * framesPerSecond / 6, 1_500_000, 12_000_000);

    /// <summary>The part kept: the start and end put in order and inside the recording, at least a second long.</summary>
    public static (TimeSpan Start, TimeSpan End) Trim(TimeSpan start, TimeSpan end, TimeSpan length)
    {
        var shortest = length < Shortest ? length : Shortest;
        if (end < start)
            (start, end) = (end, start);
        start = Clamp(start, TimeSpan.Zero, length - shortest);
        end = Clamp(end, start + shortest, length);
        return (start, end);
    }

    public static bool IsTrimmed(TimeSpan start, TimeSpan end, TimeSpan length) =>
        start > TimeSpan.FromMilliseconds(50) || end < length - TimeSpan.FromMilliseconds(50);

    /// <summary>"0:07", "12:30", "1:02:03".</summary>
    public static string Clock(TimeSpan time) =>
        time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    /// <summary>"Recording 2026-11-04 2030.mp4", or with the client's name in front.</summary>
    public static string FileName(string? clientName, DateTime now) =>
        (string.IsNullOrWhiteSpace(clientName) ? "" : ImageRules.Slug(clientName) + " ")
        + "recording " + now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ".mp4";

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high) => value < low ? low : value > high ? high : value;
}
