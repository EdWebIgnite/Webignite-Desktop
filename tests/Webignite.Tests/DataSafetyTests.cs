using System.Security.Cryptography;
using Webignite.Data;

namespace Webignite.Tests;

/// <summary>The data on disk stays readable: many writers at once, the key never replaced, restores done safely.</summary>
public sealed class DataSafetyTests : IDisposable
{
    private const string Password = "correct horse battery";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 30, 0, TimeSpan.FromHours(2));
    private readonly string root = Directory.CreateTempSubdirectory("webignite-safety-").FullName;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    private string DbPath => Path.Combine(root, "webignite.db");
    private string KeyPath => Path.Combine(root, "vault.key");

    // Stand-ins for DPAPI: reversible, and plainly not the raw key.
    private static byte[] Lock(byte[] k) => k.Select(b => (byte)(b ^ 0x5A)).ToArray();
    private static byte[] Unlock(byte[] k) => Lock(k);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Many_threads_can_write_and_read_at_once()
    {
        using var store = new LocalStore(DbPath, new Vault(key));
        Parallel.For(0, 40, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            store.Save(new StoredDocument { Id = StoredDocument.NewId(), Document = Samples.Completed(Samples.NewInvoice()) });
            Assert.NotEmpty(store.List());
        });
        Assert.Equal(40, store.List().Count);
    }

    [Fact]
    public void A_database_without_its_key_is_never_given_a_new_one()
    {
        File.WriteAllBytes(DbPath, [1, 2, 3]);
        var error = Assert.Throws<VaultKeyMissingException>(() => VaultKey.LoadOrCreate(KeyPath, DbPath, Lock, Unlock));
        Assert.Contains("backup", error.Message);
        Assert.False(File.Exists(KeyPath));
    }

    [Fact]
    public void The_first_start_makes_a_key_and_later_starts_read_the_same_one()
    {
        var made = VaultKey.LoadOrCreate(KeyPath, DbPath, Lock, Unlock);
        Assert.Equal(32, made.Length);
        Assert.NotEqual(made, File.ReadAllBytes(KeyPath));
        File.WriteAllBytes(DbPath, [1, 2, 3]);
        Assert.Equal(made, VaultKey.LoadOrCreate(KeyPath, DbPath, Lock, Unlock));
    }

    [Fact]
    public void Two_programs_starting_together_end_up_with_one_key()
    {
        var keys = new byte[8][];
        Parallel.For(0, keys.Length, i => keys[i] = VaultKey.LoadOrCreate(KeyPath, DbPath, Lock, Unlock));
        Assert.All(keys, k => Assert.Equal(keys[0], k));
    }

    private string StagedBackup()
    {
        var data = Path.Combine(root, "old");
        string file;
        using (var store = new LocalStore(Path.Combine(data, "webignite.db"), new Vault(key)))
        {
            store.Save(new StoredDocument { Id = StoredDocument.NewId(), Document = Samples.Completed(Samples.NewInvoice()) });
            file = Backups.Seal(Backups.Snapshot(store), key, Password, Path.Combine(root, "backups"), Now, "test", 1_000);
        }
        Backups.Stage(file, Password, root, Lock);
        return file;
    }

    [Fact]
    public void A_waiting_restore_keeps_its_key_locked()
    {
        StagedBackup();
        var waiting = File.ReadAllBytes(Path.Combine(root, Backups.PendingFolder, "vault.key"));
        Assert.NotEqual(key, waiting);

        byte[]? saved = null;
        Assert.True(Backups.ApplyPending(root, k => saved = k, Now, Unlock, inUse: _ => false));
        Assert.Equal(key, saved);
    }

    [Fact]
    public void A_restore_waits_while_another_program_has_the_data_open()
    {
        StagedBackup();
        File.WriteAllBytes(DbPath, [9, 9, 9]);
        Assert.Throws<BackupInUseException>(() => Backups.ApplyPending(root, _ => Assert.Fail("No key may be saved"), Now, Unlock, inUse: _ => true));

        Assert.True(Backups.HasPending(root));
        Assert.Equal([9, 9, 9], File.ReadAllBytes(DbPath));
        Assert.Empty(Directory.GetDirectories(root, "before-restore*"));
    }

    [Fact]
    public void A_failed_backup_leaves_no_half_file()
    {
        var folder = Path.Combine(root, "backups");
        var snapshot = Path.Combine(root, "snapshot");
        Directory.CreateDirectory(snapshot); // no database in it, so writing the zip fails

        Assert.ThrowsAny<Exception>(() => Backups.Seal(snapshot, key, Password, folder, Now, "test", 1_000));
        Assert.Empty(Directory.GetFiles(folder));
        Assert.False(Directory.Exists(snapshot));
    }
}
