using System.Globalization;
using System.Text;

namespace Webignite.Core.Web;

/// <summary>One speed test kept for later: the score and how long the main content took.</summary>
public sealed record SpeedRun(DateOnly Day, int Score, string? MainContent);

/// <summary>
/// Earlier speed tests per domain, so a client report can say whether the site got faster, and the text
/// for pasting into one.
/// </summary>
public static class SpeedHistory
{
    public const int Keep = 12;

    /// <summary>Adds a test. A second test on the same day replaces the first.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<SpeedRun>> Add(IReadOnlyDictionary<string, IReadOnlyList<SpeedRun>> history,
        string domain, SpeedReport report, DateOnly today)
    {
        var runs = history.GetValueOrDefault(domain, []).Where(r => r.Day != today).ToList();
        runs.Add(new SpeedRun(today, report.Score, report.Metrics.FirstOrDefault(m => m.Label == "Main content shows")?.Value));
        var copy = history.ToDictionary(h => h.Key, h => h.Value);
        copy[domain] = runs.OrderBy(r => r.Day).TakeLast(Keep).ToList();
        return copy;
    }

    /// <summary>The test before today's, if there was one.</summary>
    public static SpeedRun? Previous(IReadOnlyDictionary<string, IReadOnlyList<SpeedRun>> history, string domain, DateOnly today) =>
        history.GetValueOrDefault(domain, []).Where(r => r.Day < today).OrderBy(r => r.Day).LastOrDefault();

    /// <summary>"Up from 38 on 3 October", "Down from 61 on 3 October", "The same as on 3 October".</summary>
    public static string Compare(SpeedRun? previous, int score)
    {
        if (previous is null)
            return "";
        var when = previous.Day.ToString("d MMMM", CultureInfo.InvariantCulture);
        return score > previous.Score ? $"Up from {previous.Score} on {when}."
            : score < previous.Score ? $"Down from {previous.Score} on {when}."
            : $"The same as on {when}.";
    }

    /// <summary>Plain text for a client report or mail.</summary>
    public static string ReportText(string domain, SpeedReport report, SpeedRun? previous, DateOnly today)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Website speed for {domain}, {today.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)} (Google PageSpeed, on a phone)\n");
        text.Append(CultureInfo.InvariantCulture, $"Score: {report.Score} out of 100 ({report.Verdict.ToLowerInvariant()}).");
        if (Compare(previous, report.Score) is { Length: > 0 } change)
            text.Append(' ').Append(change);
        text.Append('\n');
        foreach (var metric in report.Metrics)
            text.Append(CultureInfo.InvariantCulture, $"{metric.Label}: {metric.Value}\n");
        if (report.Fixes.Count > 0)
        {
            text.Append("\nWhat would help most:\n");
            foreach (var fix in report.Fixes)
                text.Append("- ").Append(fix).Append('\n');
        }
        return text.ToString().TrimEnd();
    }
}
