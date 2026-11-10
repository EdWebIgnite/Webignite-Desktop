using System.Globalization;
using System.IO.Compression;

namespace Webignite.Server;

/// <summary>
/// A copy of the database every night at 02:30 South African time (00:30 UTC), taken safely while the server runs,
/// zipped into the backups folder and kept for 30 days. Switched on with WEBIGNITE_NIGHTLY_BACKUP=true, for a
/// server without cron (the Synology NAS); the xneelo set-up uses deploy/backup.sh instead.
/// </summary>
public sealed partial class NightlyBackup(ServerStore store, string folder, TimeProvider clock, ILogger<NightlyBackup> log) : BackgroundService
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);
    private static readonly TimeSpan At = new(0, 30, 0);

    /// <summary>When the next copy is due after <paramref name="now"/>.</summary>
    public static DateTimeOffset Next(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero) + At;
        return today > now ? today : today.AddDays(1);
    }

    /// <summary>Takes a copy now, removes copies older than 30 days, and gives the new copy's path.</summary>
    public static string TakeNow(ServerStore store, string folder, DateTimeOffset now)
    {
        var info = Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows())
            info.UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var name = $"webignite-server-{now.UtcDateTime.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.db";
        var copy = Path.Combine(folder, name);
        File.Delete(copy);
        store.CopyTo(copy);
        var zipped = copy + ".gz";
        using (var source = File.OpenRead(copy))
        using (var target = File.Create(zipped))
        using (var gzip = new GZipStream(target, CompressionLevel.SmallestSize))
            source.CopyTo(gzip);
        File.Delete(copy);
        foreach (var old in Directory.GetFiles(folder, "webignite-server-*.db.gz"))
        {
            if (File.GetLastWriteTimeUtc(old) < now.UtcDateTime - KeptFor)
                File.Delete(old);
        }
        return zipped;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            await Task.Delay(Next(now) - now, clock, stoppingToken).ConfigureAwait(false);
            try
            {
                var copy = TakeNow(store, folder, clock.GetUtcNow());
                BackedUp(log, copy);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                Failed(log, e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up the database to {Copy}")]
    private static partial void BackedUp(ILogger logger, string copy);

    [LoggerMessage(Level = LogLevel.Error, Message = "The nightly backup didn't work")]
    private static partial void Failed(ILogger logger, Exception error);
}
