using System.IO.Compression;
using System.Text.Json;
using Webignite.Core.Backups;

namespace Webignite.Data;

/// <summary>
/// Makes and restores encrypted backups. A backup holds a copy of the database and the key that unlocks it,
/// all sealed with the backup password, so it can be restored on a new PC. Mailbox passwords and the Xero
/// sign-in stay in Credential Manager and are not in the backup.
/// </summary>
public static class Backups
{
    public const string PendingFolder = "restore-pending";
    private const string DatabaseEntry = "webignite.db";
    private const string KeyEntry = "vault.key";
    private const string InfoEntry = "info.json";

    private sealed record Info(DateTimeOffset Made, string? AppVersion);

    /// <summary>A copy of the database taken while it's in use. Run it on the thread that uses the store.</summary>
    public static string Snapshot(LocalStore store)
    {
        var work = Path.Combine(Path.GetTempPath(), "webignite-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        store.CopyTo(Path.Combine(work, DatabaseEntry));
        return work;
    }

    /// <summary>
    /// Seals a snapshot into the backup folder and removes the oldest backups beyond <see cref="BackupRules.Keep"/>.
    /// Safe to run on a background thread. Returns the new file's path.
    /// </summary>
    public static string Seal(string snapshot, byte[] vaultKey, string password, string folder, DateTimeOffset now,
        string? appVersion = null, int iterations = BackupCrypto.DefaultIterations)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var zipPath = Path.Combine(snapshot, "backup.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Path.Combine(snapshot, DatabaseEntry), DatabaseEntry, CompressionLevel.Optimal);
                using (var key = zip.CreateEntry(KeyEntry, CompressionLevel.NoCompression).Open())
                    key.Write(vaultKey);
                using var info = zip.CreateEntry(InfoEntry).Open();
                JsonSerializer.Serialize(info, new Info(now, appVersion));
            }

            var target = Path.Combine(folder, BackupRules.FileName(now));
            var partial = target + ".partial";
            using (var plain = File.OpenRead(zipPath))
            using (var output = File.Create(partial))
                BackupCrypto.Encrypt(plain, output, password, iterations);
            File.Move(partial, target, overwrite: true);

            foreach (var old in BackupRules.ToRemove(List(folder)))
                File.Delete(old.Path);
            return target;
        }
        finally
        {
            Directory.Delete(snapshot, recursive: true);
        }
    }

    /// <summary>The app's own backup files in a folder, newest first.</summary>
    public static IReadOnlyList<BackupFile> List(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*" + BackupRules.Extension)
            .Select(path => BackupRules.MadeAt(Path.GetFileName(path)) is { } made ? new BackupFile(path, made) : null)
            .OfType<BackupFile>()
            .OrderByDescending(f => f.Made)
            .ToList();
    }

    /// <summary>
    /// Unlocks a backup and checks it opens, then leaves it ready in the data folder. Nothing is replaced yet:
    /// <see cref="ApplyPending"/> swaps it in the next time the app starts. Returns when the backup was made.
    /// </summary>
    public static DateTimeOffset Stage(string backupPath, string password, string dataRoot)
    {
        var staging = Path.Combine(dataRoot, PendingFolder + ".tmp");
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            var zipPath = Path.Combine(staging, "backup.zip");
            using (var input = File.OpenRead(backupPath))
            using (var plain = File.Create(zipPath))
                BackupCrypto.Decrypt(input, plain, password);

            Info info;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var database = zip.GetEntry(DatabaseEntry) ?? throw new InvalidDataException("The backup has no database in it.");
                var keyEntry = zip.GetEntry(KeyEntry) ?? throw new InvalidDataException("The backup has no key in it.");
                database.ExtractToFile(Path.Combine(staging, DatabaseEntry));
                keyEntry.ExtractToFile(Path.Combine(staging, KeyEntry));
                using var infoStream = zip.GetEntry(InfoEntry)?.Open() ?? throw new InvalidDataException("The backup is incomplete.");
                info = JsonSerializer.Deserialize<Info>(infoStream) ?? throw new InvalidDataException("The backup is incomplete.");
            }
            File.Delete(zipPath);

            // Prove the key opens the database before anything is replaced.
            var key = File.ReadAllBytes(Path.Combine(staging, KeyEntry));
            using (var store = new LocalStore(Path.Combine(staging, DatabaseEntry), new Vault(key)))
                store.Check();

            var pending = Path.Combine(dataRoot, PendingFolder);
            if (Directory.Exists(pending))
                Directory.Delete(pending, recursive: true);
            Directory.Move(staging, pending);
            return info.Made;
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    /// <summary>True when a restored backup is waiting to be swapped in.</summary>
    public static bool HasPending(string dataRoot) =>
        File.Exists(Path.Combine(dataRoot, PendingFolder, DatabaseEntry));

    /// <summary>
    /// Runs before the database is opened. Moves the current database aside into a "before-restore" folder,
    /// puts the restored one in its place and saves its key. Returns false when nothing was waiting.
    /// </summary>
    public static bool ApplyPending(string dataRoot, Action<byte[]> saveKey, DateTimeOffset now)
    {
        var pending = Path.Combine(dataRoot, PendingFolder);
        if (!HasPending(dataRoot))
            return false;

        var aside = Path.Combine(dataRoot, "before-restore " + now.ToLocalTime().ToString("yyyy-MM-dd HHmmss"));
        Directory.CreateDirectory(aside);
        foreach (var name in new[] { "webignite.db", "webignite.db-wal", "webignite.db-shm", "vault.key" })
        {
            var current = Path.Combine(dataRoot, name);
            if (File.Exists(current))
                File.Copy(current, Path.Combine(aside, name), overwrite: true);
        }
        foreach (var name in new[] { "webignite.db-wal", "webignite.db-shm" })
            File.Delete(Path.Combine(dataRoot, name));

        File.Copy(Path.Combine(pending, DatabaseEntry), Path.Combine(dataRoot, "webignite.db"), overwrite: true);
        saveKey(File.ReadAllBytes(Path.Combine(pending, KeyEntry)));
        Directory.Delete(pending, recursive: true);
        return true;
    }
}
