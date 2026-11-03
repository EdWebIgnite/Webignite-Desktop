using System.IO.Compression;
using System.IO.Pipes;
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
        var target = Path.Combine(folder, BackupRules.FileName(now));
        var partial = target + ".partial";
        try
        {
            Directory.CreateDirectory(folder);
            // The zip holds the database and its key, so it never touches the disk unencrypted: it is written
            // into a pipe on one thread and encrypted straight into the backup file on this one.
            using (var pipe = new AnonymousPipeServerStream(PipeDirection.Out))
            using (var reader = new AnonymousPipeClientStream(PipeDirection.In, pipe.ClientSafePipeHandle))
            {
                var writing = Task.Run(() =>
                {
                    try
                    {
                        using var zip = new ZipArchive(pipe, ZipArchiveMode.Create, leaveOpen: true);
                        zip.CreateEntryFromFile(Path.Combine(snapshot, DatabaseEntry), DatabaseEntry, CompressionLevel.Optimal);
                        using (var key = zip.CreateEntry(KeyEntry, CompressionLevel.NoCompression).Open())
                            key.Write(vaultKey);
                        using var info = zip.CreateEntry(InfoEntry).Open();
                        JsonSerializer.Serialize(info, new Info(now, appVersion));
                    }
                    finally
                    {
                        pipe.Dispose(); // ends the stream for the reader, also when writing failed
                    }
                });
                using (var output = File.Create(partial))
                    BackupCrypto.Encrypt(reader, output, password, iterations);
                writing.GetAwaiter().GetResult();
            }
            File.Move(partial, target, overwrite: true);

            foreach (var old in BackupRules.ToRemove(List(folder)))
                File.Delete(old.Path);
            return target;
        }
        catch
        {
            // Don't leave half a backup behind.
            if (File.Exists(partial))
                File.Delete(partial);
            throw;
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
    /// <param name="protectKey">
    /// How the key is kept while it waits for the next start: on Windows, locked to the signed-in user (DPAPI), so a
    /// restore left waiting for days doesn't leave the raw key in a file. Tests pass nothing and keep it as is.
    /// </param>
    public static DateTimeOffset Stage(string backupPath, string password, string dataRoot, Func<byte[], byte[]>? protectKey = null)
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
            byte[] key;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var database = zip.GetEntry(DatabaseEntry) ?? throw new InvalidDataException("The backup has no database in it.");
                var keyEntry = zip.GetEntry(KeyEntry) ?? throw new InvalidDataException("The backup has no key in it.");
                database.ExtractToFile(Path.Combine(staging, DatabaseEntry));
                using var keyStream = keyEntry.Open();
                using var keyBytes = new MemoryStream();
                keyStream.CopyTo(keyBytes);
                key = keyBytes.ToArray();
                using var infoStream = zip.GetEntry(InfoEntry)?.Open() ?? throw new InvalidDataException("The backup is incomplete.");
                info = JsonSerializer.Deserialize<Info>(infoStream) ?? throw new InvalidDataException("The backup is incomplete.");
            }
            File.Delete(zipPath); // it holds the raw key

            // Prove the key opens the database before anything is replaced.
            using (var store = new LocalStore(Path.Combine(staging, DatabaseEntry), new Vault(key)))
                store.Check();
            File.WriteAllBytes(Path.Combine(staging, KeyEntry), (protectKey ?? (k => k))(key));

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
    /// <remarks>
    /// Throws <see cref="BackupInUseException"/>, changing nothing, when another program still has the database
    /// open: the Claude connector stays running while Claude Desktop is open. Swapping under it would fail, or
    /// let it write into the restored database with the old key. The restore stays waiting for the next start.
    /// </remarks>
    public static bool ApplyPending(string dataRoot, Action<byte[]> saveKey, DateTimeOffset now,
        Func<byte[], byte[]>? unprotectKey = null, Func<string, bool>? inUse = null)
    {
        var pending = Path.Combine(dataRoot, PendingFolder);
        if (!HasPending(dataRoot))
            return false;
        var database = Path.Combine(dataRoot, "webignite.db");
        if ((inUse ?? IsInUse)(database))
            throw new BackupInUseException();
        var restoredKey = (unprotectKey ?? (k => k))(File.ReadAllBytes(Path.Combine(pending, KeyEntry)));

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

        File.Copy(Path.Combine(pending, DatabaseEntry), database, overwrite: true);
        saveKey(restoredKey);
        Directory.Delete(pending, recursive: true);
        return true;
    }

    /// <summary>Whether another program has the file open. Windows refuses a second open that shares nothing.</summary>
    internal static bool IsInUse(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}

/// <summary>A restore can't be swapped in while another program (usually Claude Desktop's connector) has the data open.</summary>
public sealed class BackupInUseException() : IOException(
    "The restored backup is waiting, but Claude Desktop still has Webignite's data open. " +
    "Quit Claude Desktop fully from the tray, then start Webignite again to finish the restore. Nothing was changed.");
