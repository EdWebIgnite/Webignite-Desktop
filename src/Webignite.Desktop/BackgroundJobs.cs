using Avalonia.Threading;

namespace Webignite.Desktop;

/// <summary>
/// What runs on its own while the app is open, whichever pages are open or not: mail checks, payments from
/// Xero, website checks, syncing shared projects and the daily backup. One timer looks once a minute at what is due. The jobs
/// themselves are handed in, so they can tell whatever pages are open what changed.
/// </summary>
public sealed class BackgroundJobs(AppServices services) : IDisposable
{
    private readonly DispatcherTimer minute = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer assistants = new() { Interval = TimeSpan.FromSeconds(3) };
    private DateTimeOffset assistantsSeen;
    private DateTimeOffset lastBackupLook = DateTimeOffset.Now;

    public Func<Task>? CheckMail { get; init; }
    public Func<Task>? CheckPayments { get; init; }
    public Func<Task>? CheckSites { get; init; }
    public Func<Task>? CheckXeroClients { get; init; }
    public Func<Task>? CheckXeroDocuments { get; init; }
    public Func<Task>? CheckXeroBills { get; init; }

    /// <summary>Syncs shared projects; run every minute while signed in to a sync server.</summary>
    public Func<Task>? SyncProjects { get; init; }

    /// <summary>
    /// Claude or ChatGPT changed something through the connector (another process, the same database): which
    /// areas, e.g. projects or todos. Looked for every few seconds; reading the note is one small query.
    /// </summary>
    public Action<IReadOnlyList<string>>? AssistantChanged { get; init; }

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
        if (AssistantChanged is not null)
        {
            assistantsSeen = ReadSignal()?.At ?? DateTimeOffset.MinValue;
            assistants.Tick += AssistantsTick;
            assistants.Start();
        }
    }

    private void AssistantsTick(object? sender, EventArgs e) => LookForAssistants();

    /// <summary>What the timer does every few seconds: if Claude or ChatGPT changed something since, says which parts.</summary>
    public void LookForAssistants()
    {
        if (ReadSignal() is not { } mark || mark.At <= assistantsSeen)
            return;
        var areas = mark.Since(assistantsSeen);
        assistantsSeen = mark.At;
        if (areas.Count > 0)
            AssistantChanged?.Invoke(areas);
    }

    // The connector may be writing at the same moment; a failed read is simply tried again next time.
    private Webignite.Data.AssistantSignal.Mark? ReadSignal()
    {
        try
        {
            return Webignite.Data.AssistantSignal.Read(services.Store);
        }
        catch (Exception problem) when (problem is Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        {
            Webignite.Data.AppLog.Write("Looking for changes from Claude or ChatGPT", problem);
            return null;
        }
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
        // Xero's customers come in as clients every few hours, and on the first minute after start.
        if (CheckXeroClients is not null && services.XeroApi is not null && services.XeroClients.Due(now))
            CheckXeroClients().Forget("Bringing clients in from Xero");
        // Quotes and invoices made in Xero itself come in every few hours, and on the first minute after start.
        if (CheckXeroDocuments is not null && services.XeroApi is not null && services.XeroDocuments.Due(now))
            CheckXeroDocuments().Forget("Bringing in quotes and invoices from Xero");
        // Bills still to pay are read from Xero every few hours too.
        if (CheckXeroBills is not null && services.XeroApi is not null && services.XeroBills.Due(now))
            CheckXeroBills().Forget("Reading bills from Xero");
        // Client websites are visited every 15 minutes.
        if (CheckSites is not null && services.SiteWatch.Due(now))
            CheckSites().Forget("Checking client websites");
        // Shared projects sync every minute while signed in; a sync still running is left to finish.
        if (SyncProjects is not null && services.MaySync)
            SyncProjects().Forget("Syncing shared projects");
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
        assistants.Stop();
        assistants.Tick -= AssistantsTick;
    }
}
