using System.Globalization;
using Webignite.Core.Clients;

namespace Webignite.Core.Web;

/// <summary>One visit to a watched site's home page.</summary>
public sealed record UptimePing(DateTimeOffset At, bool Up, int? Status, int Milliseconds, DateTimeOffset? CertificateExpires = null, string? Problem = null);

/// <summary>Checks and up checks on one day, for the uptime figure.</summary>
public sealed record UptimeDay(DateOnly Day, int Checks, int Up);

/// <summary>What is known about one watched site.</summary>
public sealed record WatchedSite
{
    public required string Domain { get; init; }
    public UptimePing? Last { get; init; }
    /// <summary>Failed checks in a row. One failure can be a blip; two in a row counts as down.</summary>
    public int Failures { get; init; }
    public DateTimeOffset? DownSince { get; init; }
    /// <summary>Whether the "down" alert has gone out for this outage.</summary>
    public bool DownAlerted { get; init; }
    /// <summary>The certificate expiry date already warned about, so the warning goes out once per certificate.</summary>
    public DateOnly? CertificateAlerted { get; init; }
    public IReadOnlyList<UptimeDay> Days { get; init; } = [];
}

/// <summary>Which sites are watched, and what was found. Client websites are watched unless switched off.</summary>
public sealed record UptimeSettings
{
    public bool On { get; init; } = true;
    public int IntervalMinutes { get; init; } = 15;
    /// <summary>Sites that aren't on a client page, such as Webignite's own.</summary>
    public IReadOnlyList<string> Extra { get; init; } = [];
    /// <summary>Client websites not to watch.</summary>
    public IReadOnlyList<string> Off { get; init; } = [];
    public IReadOnlyDictionary<string, WatchedSite> Sites { get; init; } = new Dictionary<string, WatchedSite>();
}

/// <summary>A note to show when a site goes down, comes back, or its certificate is about to run out.</summary>
public sealed record UptimeAlert(string Domain, string Title, string Text, bool Problem);

/// <summary>A watched site with the client it belongs to, if any.</summary>
public sealed record WatchedDomain(string Domain, string? Client);

/// <summary>
/// Watching client websites: a visit to each home page every 15 minutes while the app is open. Two failed
/// visits in a row count as down (one can be a blip), and the certificate is warned about two weeks out.
/// </summary>
public static class Uptime
{
    public const int FailuresForDown = 2;
    public const int KeepDays = 30;
    public const int SlowMilliseconds = 5000;

    /// <summary>Client websites plus the extra sites, without the ones switched off. Each domain once.</summary>
    public static IReadOnlyList<WatchedDomain> Domains(IEnumerable<Client> clients, UptimeSettings settings)
    {
        var off = new HashSet<string>(settings.Off, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<WatchedDomain>();
        foreach (var client in clients.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (DomainReport.Normalise(client.Website) is { } domain && !off.Contains(domain) && seen.Add(domain))
                list.Add(new(domain, client.Name));
        }
        foreach (var extra in settings.Extra)
        {
            if (DomainReport.Normalise(extra) is { } domain && !off.Contains(domain) && seen.Add(domain))
                list.Add(new(domain, null));
        }
        return list;
    }

    /// <summary>Adds a site to watch. A client's site that was switched off is switched back on instead.</summary>
    public static UptimeSettings Add(UptimeSettings settings, string domain, IEnumerable<Client> clients)
    {
        if (settings.Off.Contains(domain, StringComparer.OrdinalIgnoreCase))
            return settings with { Off = settings.Off.Where(d => !string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)).ToList() };
        if (clients.Any(c => string.Equals(DomainReport.Normalise(c.Website), domain, StringComparison.OrdinalIgnoreCase))
            || settings.Extra.Contains(domain, StringComparer.OrdinalIgnoreCase))
            return settings;
        return settings with { Extra = [.. settings.Extra, domain] };
    }

    /// <summary>Stops watching a site and forgets what was found.</summary>
    public static UptimeSettings Remove(UptimeSettings settings, string domain, IEnumerable<Client> clients)
    {
        var sites = settings.Sites.Where(s => !string.Equals(s.Key, domain, StringComparison.OrdinalIgnoreCase)).ToDictionary(s => s.Key, s => s.Value);
        var isClient = clients.Any(c => string.Equals(DomainReport.Normalise(c.Website), domain, StringComparison.OrdinalIgnoreCase));
        return settings with
        {
            Extra = settings.Extra.Where(d => !string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)).ToList(),
            Off = isClient && !settings.Off.Contains(domain, StringComparer.OrdinalIgnoreCase) ? [.. settings.Off, domain] : settings.Off,
            Sites = sites,
        };
    }

    /// <summary>Whether the checks are due.</summary>
    public static bool Due(UptimeSettings settings, DateTimeOffset? lastRun, DateTimeOffset now) =>
        settings.On && (lastRun is null || now - lastRun.Value >= TimeSpan.FromMinutes(Math.Max(5, settings.IntervalMinutes)));

    /// <summary>Adds a visit to what's known about the site, and says whether anyone needs to hear about it.</summary>
    public static (WatchedSite Site, UptimeAlert? Alert) Record(WatchedSite? site, string domain, UptimePing ping, DateOnly today)
    {
        site ??= new WatchedSite { Domain = domain };
        var days = site.Days.Where(d => d.Day > today.AddDays(-KeepDays)).ToList();
        var index = days.FindIndex(d => d.Day == today);
        var day = index >= 0 ? days[index] : new UptimeDay(today, 0, 0);
        day = day with { Checks = day.Checks + 1, Up = day.Up + (ping.Up ? 1 : 0) };
        if (index >= 0)
            days[index] = day;
        else
            days.Add(day);

        UptimeAlert? alert = null;
        var updated = site with { Last = ping, Days = days };
        if (ping.Up)
        {
            if (site.DownAlerted && site.DownSince is { } since)
                alert = new(domain, $"{domain} is back up", $"It was down for {Duration(ping.At - since)}.", Problem: false);
            updated = updated with { Failures = 0, DownSince = null, DownAlerted = false };
        }
        else
        {
            var failures = site.Failures + 1;
            var downSince = site.DownSince ?? ping.At;
            var confirmed = failures >= FailuresForDown;
            if (confirmed && !site.DownAlerted)
                alert = new(domain, $"{domain} is down", ping.Problem ?? "The site didn't answer.", Problem: true);
            updated = updated with { Failures = failures, DownSince = downSince, DownAlerted = site.DownAlerted || confirmed };
        }

        if (alert is null && ping.CertificateExpires is { } expires)
        {
            var expiryDay = DateOnly.FromDateTime(expires.UtcDateTime);
            var left = expiryDay.DayNumber - today.DayNumber;
            if (left <= DomainReport.CertificateWarnDays && site.CertificateAlerted != expiryDay)
            {
                alert = new(domain, $"Certificate on {domain} runs out soon", CertificateText(left), Problem: true);
                updated = updated with { CertificateAlerted = expiryDay };
            }
        }
        return (updated, alert);
    }

    /// <summary>Whether the site counts as down: two failed visits in a row.</summary>
    public static bool IsDown(WatchedSite site) => site.Failures >= FailuresForDown;

    /// <summary>Share of visits in the last 30 days that found the site up, or null before the first visit.</summary>
    public static double? Percent(WatchedSite site)
    {
        var checks = site.Days.Sum(d => d.Checks);
        return checks == 0 ? null : 100.0 * site.Days.Sum(d => d.Up) / checks;
    }

    public static string PercentText(WatchedSite site) => Percent(site) is { } percent
        ? (percent >= 99.95 ? "100" : percent.ToString(percent >= 90 ? "0.0" : "0", CultureInfo.InvariantCulture)) + "% up"
        : "";

    /// <summary>"Up, 420 ms", "Down since 09:15", "Not checked yet".</summary>
    public static string Status(WatchedSite? site, DateTimeOffset now)
    {
        if (site?.Last is not { } last)
            return "Not checked yet";
        if (IsDown(site))
            return $"Down since {When(site.DownSince ?? last.At, now)}";
        if (!last.Up)
            return "Didn't answer once. Checking again.";
        return last.Milliseconds >= SlowMilliseconds
            ? "Up but slow, " + (last.Milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : $"Up, {last.Milliseconds} ms";
    }

    /// <summary>Days left on the certificate, as a short note. Empty when there's plenty of time.</summary>
    public static string Certificate(WatchedSite? site, DateOnly today)
    {
        if (site?.Last?.CertificateExpires is not { } expires)
            return "";
        var left = DateOnly.FromDateTime(expires.UtcDateTime).DayNumber - today.DayNumber;
        return left > DomainReport.CertificateWarnDays ? $"Certificate fine for {left} days" : CertificateText(left);
    }

    public static bool CertificateProblem(WatchedSite? site, DateOnly today) =>
        site?.Last?.CertificateExpires is { } expires
        && DateOnly.FromDateTime(expires.UtcDateTime).DayNumber - today.DayNumber <= DomainReport.CertificateWarnDays;

    private static string CertificateText(int left) => left switch
    {
        < 0 => "The certificate has run out. Visitors get a security warning.",
        0 => "The certificate runs out today.",
        1 => "The certificate runs out tomorrow.",
        _ => $"The certificate runs out in {left} days.",
    };

    private static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        return local.Date == now.ToLocalTime().Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("d MMMM HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Duration(TimeSpan span)
    {
        if (span.TotalMinutes < 60)
            return Count((int)Math.Max(1, Math.Round(span.TotalMinutes)), "minute");
        if (span.TotalHours < 48)
            return Count((int)Math.Round(span.TotalHours), "hour");
        return Count((int)span.TotalDays, "day");
    }

    private static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
