using Webignite.Core.Clients;
using Webignite.Core.Web;

namespace Webignite.Services;

/// <summary>
/// Visits every watched site, records what it found and returns what needs announcing. The visit itself
/// is handed in (<see cref="SiteChecker.PingAsync"/> in the app), so tests can answer for a site.
/// </summary>
public sealed class UptimeWatch(
    Func<IReadOnlyList<Client>> clients,
    Func<UptimeSettings> settings,
    Action<UptimeSettings> save,
    Func<string, CancellationToken, Task<UptimePing>>? ping = null,
    TimeProvider? clock = null)
{
    private readonly Func<string, CancellationToken, Task<UptimePing>> ping = ping ?? SiteChecker.PingAsync;
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private bool running;

    public DateTimeOffset? LastRun { get; private set; }

    public bool Running => running;

    public bool Due(DateTimeOffset now) => Uptime.Due(settings(), LastRun, now);

    public async Task<IReadOnlyList<UptimeAlert>> RunAsync(CancellationToken cancel = default)
    {
        if (running)
            return [];
        running = true;
        LastRun = clock.GetLocalNow();
        try
        {
            var domains = Uptime.Domains(clients(), settings());
            using var gate = new SemaphoreSlim(4);
            var pings = await Task.WhenAll(domains.Select(async d =>
            {
                await gate.WaitAsync(cancel);
                try
                {
                    return (d.Domain, Ping: await ping(d.Domain, cancel));
                }
                finally
                {
                    gate.Release();
                }
            }));

            // Read the settings again: a site may have been added or removed while the visits ran.
            var current = settings();
            var still = Uptime.Domains(clients(), current).Select(d => d.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sites = current.Sites.Where(s => still.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
            var alerts = new List<UptimeAlert>();
            var today = DateOnly.FromDateTime(clock.GetLocalNow().Date);
            foreach (var (domain, result) in pings.Where(p => still.Contains(p.Domain)))
            {
                var (site, alert) = Uptime.Record(sites.GetValueOrDefault(domain), domain, result, today);
                sites[domain] = site;
                if (alert is not null)
                    alerts.Add(alert);
            }
            save(current with { Sites = sites });
            return alerts;
        }
        finally
        {
            running = false;
        }
    }
}
