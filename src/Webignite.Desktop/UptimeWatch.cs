using Webignite.Core.Web;

namespace Webignite.Desktop;

/// <summary>Visits every watched site, records what it found and returns what needs announcing.</summary>
public static class UptimeWatch
{
    private static bool running;

    public static DateTimeOffset? LastRun { get; private set; }

    public static bool Running => running;

    public static async Task<IReadOnlyList<UptimeAlert>> RunAsync(AppServices services, CancellationToken cancel = default)
    {
        if (running)
            return [];
        running = true;
        LastRun = DateTimeOffset.Now;
        try
        {
            var domains = Uptime.Domains(services.Store.ListClients(), services.UptimeSettings);
            using var gate = new SemaphoreSlim(4);
            var pings = await Task.WhenAll(domains.Select(async d =>
            {
                await gate.WaitAsync(cancel);
                try
                {
                    return (d.Domain, Ping: await SiteChecker.PingAsync(d.Domain, cancel));
                }
                finally
                {
                    gate.Release();
                }
            }));

            // Read the settings again: a site may have been added or removed while the visits ran.
            var settings = services.UptimeSettings;
            var still = Uptime.Domains(services.Store.ListClients(), settings).Select(d => d.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sites = settings.Sites.Where(s => still.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
            var alerts = new List<UptimeAlert>();
            var today = DateOnly.FromDateTime(DateTime.Today);
            foreach (var (domain, ping) in pings.Where(p => still.Contains(p.Domain)))
            {
                var (site, alert) = Uptime.Record(sites.GetValueOrDefault(domain), domain, ping, today);
                sites[domain] = site;
                if (alert is not null)
                    alerts.Add(alert);
            }
            services.UptimeSettings = settings with { Sites = sites };
            return alerts;
        }
        finally
        {
            running = false;
        }
    }
}
