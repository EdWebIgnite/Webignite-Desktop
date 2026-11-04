using Avalonia.Threading;

namespace Webignite.Desktop;

/// <summary>
/// What runs on its own while the app is open, whichever pages are open or not: mail checks, payments from
/// Xero, website checks and the daily backup. One timer looks once a minute at what is due. The jobs
/// themselves are handed in, so they can tell whatever pages are open what changed.
/// </summary>
public sealed class BackgroundJobs(AppServices services) : IDisposable
{
    private readonly DispatcherTimer minute = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTimeOffset lastBackupLook = DateTimeOffset.Now;

    public Func<Task>? CheckMail { get; init; }
    public Func<Task>? CheckPayments { get; init; }
    public Func<Task>? CheckSites { get; init; }

    /// <summary>Runs once a minute after the jobs, for small housekeeping such as letting unused pages go.</summary>
    public Action? EveryMinute { get; init; }

    /// <summary>When mail was last checked, by the timer or by hand.</summary>
    public DateTimeOffset LastMailCheck { get; set; } = DateTimeOffset.MinValue;

    /// <summary>Starts the timer, backs up if due and checks mail straight away.</summary>
    public void Start()
    {
        services.BackUpAsync().Forget("Backing up");
        if (CheckMail is not null)
            CheckMail().Forget("Checking mail");
        minute.Tick += Tick;
        minute.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        services.CatchUp(); // a moment's work only after midnight; otherwise it returns at once
        // Mail is checked every few minutes (Settings), even when another page is open.
        if (CheckMail is not null && now - LastMailCheck >= Webignite.Core.Mail.NewMail.Interval(services.Settings.MailCheck))
            CheckMail().Forget("Checking mail");
        // Payments are read from Xero every half hour.
        if (CheckPayments is not null && services.XeroApi is not null && services.Payments.Due(now))
            CheckPayments().Forget("Checking Xero for payments");
        // Client websites are visited every 15 minutes.
        if (CheckSites is not null && services.SiteWatch.Due(now))
            CheckSites().Forget("Checking client websites");
        // Once an hour, whether the daily backup is due, for when the app stays open for days.
        if (now - lastBackupLook >= TimeSpan.FromHours(1))
        {
            lastBackupLook = now;
            services.BackUpAsync().Forget("Backing up");
        }
        EveryMinute?.Invoke();
    }

    public void Dispose()
    {
        minute.Stop();
        minute.Tick -= Tick;
    }
}
