using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Webignite.Server;

namespace Webignite.Tests;

/// <summary>A server set up without a settings file (the Synology NAS): its own secret and licence key, and nightly copies.</summary>
public sealed class ServerFilesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-server-files-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void A_secret_is_made_once_kept_for_the_server_alone_and_read_back_after()
    {
        var made = 0;
        var first = ServerFiles.Kept(folder, ServerFiles.Secret, () => { made++; return "first value"; });
        var again = ServerFiles.Kept(folder, ServerFiles.Secret, () => { made++; return "second value"; });
        Assert.Equal("first value", first);
        Assert.Equal("first value", again);
        Assert.Equal(1, made);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(folder, ServerFiles.Secret)));
    }

    [Fact]
    public void The_nightly_copy_is_due_at_half_past_midnight_UTC()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 30, 0, TimeSpan.Zero), NightlyBackup.Next(new DateTimeOffset(2026, 11, 10, 0, 10, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 0, 30, 0, TimeSpan.Zero), NightlyBackup.Next(new DateTimeOffset(2026, 11, 10, 0, 30, 0, TimeSpan.Zero)));
        // 08:00 in South Africa is 06:00 UTC: the next copy is tonight.
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 0, 30, 0, TimeSpan.Zero), NightlyBackup.Next(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void A_nightly_copy_opens_as_the_database_and_copies_past_30_days_go()
    {
        var store = new ServerStore(Path.Combine(folder, "webignite-server.db"));
        var backups = Path.Combine(folder, "backups");
        Directory.CreateDirectory(backups);
        var old = Path.Combine(backups, "webignite-server-2026-09-01-0030.db.gz");
        File.WriteAllBytes(old, [1]);
        File.SetLastWriteTimeUtc(old, new DateTime(2026, 9, 1, 0, 30, 0, DateTimeKind.Utc));
        var now = new DateTimeOffset(2026, 11, 10, 0, 30, 0, TimeSpan.Zero);

        var copy = NightlyBackup.TakeNow(store, backups, now);

        Assert.EndsWith("webignite-server-2026-11-10-0030.db.gz", copy, StringComparison.Ordinal);
        Assert.False(File.Exists(old));
        var opened = Path.Combine(folder, "opened.db");
        using (var gzip = new GZipStream(File.OpenRead(copy), CompressionMode.Decompress))
        using (var target = File.Create(opened))
            gzip.CopyTo(target);
        using var db = new SqliteConnection($"Data Source={opened}");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'accounts'";
        Assert.Equal(1L, command.ExecuteScalar());
    }
}
