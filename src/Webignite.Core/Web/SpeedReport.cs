using System.Globalization;
using System.Text.Json;

namespace Webignite.Core.Web;

public sealed record SpeedMetric(string Label, string Value, bool Problem);

/// <summary>Google's PageSpeed result, as a score out of 100, the main timings and what would help most.</summary>
public sealed record SpeedReport(int Score, IReadOnlyList<SpeedMetric> Metrics, IReadOnlyList<string> Fixes)
{
    /// <summary>Google's own bands: 90 and up is good, under 50 is poor.</summary>
    public string Verdict => Score >= 90 ? "Fast" : Score >= 50 ? "Needs work" : "Slow";
    public bool Problem => Score < 50;

    public const int MaxFixes = 5;

    // The timings shown, with Google's "good" limits (ms, or unitless for layout shift).
    private static readonly (string Id, string Label, double Good)[] Shown =
    [
        ("largest-contentful-paint", "Main content shows", 2500),
        ("first-contentful-paint", "First thing shows", 1800),
        ("total-blocking-time", "Page frozen for", 200),
        ("cumulative-layout-shift", "Things jumping about", 0.1),
        ("speed-index", "Looks loaded", 3400),
    ];

    public static SpeedReport Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var lighthouse = doc.RootElement.GetProperty("lighthouseResult");
        var score = lighthouse.GetProperty("categories").GetProperty("performance").TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(s.GetDouble() * 100)
            : 0;
        var audits = lighthouse.GetProperty("audits");

        var metrics = new List<SpeedMetric>();
        foreach (var (id, label, good) in Shown)
        {
            if (!audits.TryGetProperty(id, out var audit))
                continue;
            var shown = audit.TryGetProperty("displayValue", out var d) ? (d.GetString() ?? "").Replace(' ', ' ') : "";
            var value = audit.TryGetProperty("numericValue", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 0;
            metrics.Add(new SpeedMetric(label, shown, value > good));
        }

        // What would save the most time, biggest first.
        var fixes = new List<(double Saving, string Text)>();
        foreach (var audit in audits.EnumerateObject())
        {
            var a = audit.Value;
            if (!a.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Object
                || !details.TryGetProperty("type", out var type) || type.GetString() != "opportunity")
                continue;
            var saving = details.TryGetProperty("overallSavingsMs", out var ms) && ms.ValueKind == JsonValueKind.Number ? ms.GetDouble() : 0;
            if (saving < 100 || !a.TryGetProperty("title", out var title))
                continue;
            fixes.Add((saving, $"{title.GetString()} (saves about {Seconds(saving)})"));
        }

        return new SpeedReport(score, metrics, fixes.OrderByDescending(f => f.Saving).Take(MaxFixes).Select(f => f.Text).ToList());
    }

    private static string Seconds(double ms) =>
        ms < 1000 ? $"{Math.Round(ms / 10) * 10:0} ms" : string.Create(CultureInfo.InvariantCulture, $"{ms / 1000:0.0} s");

    /// <summary>The address Google is asked to test, with https:// added when it's missing.</summary>
    public static string? Address(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.')
            ? uri.ToString()
            : null;
    }
}
