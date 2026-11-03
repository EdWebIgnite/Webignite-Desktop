using System.Security.Cryptography;
using System.Text.Json;
using Webignite.Data;

namespace Webignite.Tests;

/// <summary>What keeps a later version of the app able to open data an earlier version saved, and the other way round.</summary>
public sealed class StoredDataTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    private LocalStore Open() => new(Path.Combine(folder, "test.db"), new Vault(key));

    private sealed record WideSetting(string Name, int Count, string Extra);
    private sealed record NarrowSetting(string Name, int Count);
    private sealed record NewerSetting(string Name, int Count, string? AddedLater);

    [Fact]
    public void A_field_this_version_does_not_know_is_skipped_not_refused()
    {
        // The next version of the app saved a setting with one more field, then the person went back to this one.
        using var store = Open();
        store.SetSetting("look", new WideSetting("Graphite", 3, "from a newer version"));

        var read = store.GetSetting<NarrowSetting>("look");

        Assert.Equal(new NarrowSetting("Graphite", 3), read);
    }

    [Fact]
    public void A_field_added_later_reads_as_null_from_older_data()
    {
        using var store = Open();
        store.SetSetting("look", new NarrowSetting("Graphite", 3));

        var read = store.GetSetting<NewerSetting>("look");

        Assert.Equal(new NewerSetting("Graphite", 3, null), read);
    }

    [Fact]
    public void Assistant_input_stays_strict()
    {
        // The strictness is still right for what Claude and ChatGPT send: a typo there must not drop data quietly.
        var json = """{"kind":"invoice","customer":{"name":"Karoo"},"lines":[],"totl":1}""";
        Assert.Throws<JsonException>(() => DocumentJson.Parse(json));
    }

    [Fact]
    public void Stored_records_use_the_same_snake_case_shape_as_documents()
    {
        var json = JsonSerializer.Serialize(new NewerSetting("Graphite", 3, "x"), StoredJson.Options);
        Assert.Equal("""{"name":"Graphite","count":3,"added_later":"x"}""", json);
    }

    [Fact]
    public void A_new_database_is_stamped_with_the_current_schema_version()
    {
        using var store = Open();
        Assert.Equal(LocalStore.SchemaVersion, store.StoredSchemaVersion);
    }

    [Fact]
    public void An_older_database_is_brought_up_to_date_on_open()
    {
        var path = Path.Combine(folder, "old.db");
        using (var store = new LocalStore(path, new Vault(key)))
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 0";
            command.ExecuteNonQuery();
        }
        using var again = new LocalStore(path, new Vault(key));
        Assert.Equal(LocalStore.SchemaVersion, again.StoredSchemaVersion);
    }

    [Fact]
    public void Log_lines_carry_the_time_what_happened_and_the_error()
    {
        AppLog.UseRoot(folder);
        try
        {
            var now = new DateTimeOffset(2026, 11, 3, 9, 15, 0, TimeSpan.FromHours(2));
            AppLog.Write("Checking mail failed", new InvalidOperationException("No connection"), now);

            var text = File.ReadAllText(AppLog.CurrentFile(now));
            Assert.StartsWith("2026-11-03 09:15:00 +02:00  Checking mail failed", text);
            Assert.Contains("InvalidOperationException: No connection", text);
            Assert.Equal(Path.Combine(folder, "logs", "webignite-2026-11-03.log"), AppLog.CurrentFile(now));
        }
        finally
        {
            AppLog.UseRoot(null);
        }
    }

    [Fact]
    public void Old_log_files_are_removed_and_recent_ones_kept()
    {
        AppLog.UseRoot(folder);
        try
        {
            var now = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(2));
            Directory.CreateDirectory(AppLog.Folder);
            File.WriteAllText(Path.Combine(AppLog.Folder, "webignite-2026-10-01.log"), "old");
            File.WriteAllText(Path.Combine(AppLog.Folder, "webignite-2026-10-25.log"), "recent");
            File.WriteAllText(Path.Combine(AppLog.Folder, "notes.txt"), "not ours");

            AppLog.Trim(now);

            Assert.False(File.Exists(Path.Combine(AppLog.Folder, "webignite-2026-10-01.log")));
            Assert.True(File.Exists(Path.Combine(AppLog.Folder, "webignite-2026-10-25.log")));
            Assert.True(File.Exists(Path.Combine(AppLog.Folder, "notes.txt")));
        }
        finally
        {
            AppLog.UseRoot(null);
        }
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
