using System.Security.Cryptography;
using Webignite.Core.Backups;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class BackupTests : IDisposable
{
    private const string Password = "correct horse battery";
    private const int FastIterations = 1_000;
    private readonly string root = Directory.CreateTempSubdirectory("webignite-backup-").FullName;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 30, 0, TimeSpan.FromHours(2));

    private string Data => Path.Combine(root, "data");
    private string Folder => Path.Combine(root, "backups");
    private LocalStore Open(byte[]? withKey = null) => new(Path.Combine(Data, "webignite.db"), new Vault(withKey ?? key));

    private static StoredDocument Stored(Document doc) => new() { Id = StoredDocument.NewId(), Document = doc };

    private string MakeBackup(LocalStore store, DateTimeOffset? at = null) =>
        Backups.Seal(Backups.Snapshot(store), key, Password, Folder, at ?? Now, "test", FastIterations);

    public void Dispose() => Directory.Delete(root, recursive: true);

    // The file format

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(BackupCrypto.PieceSize)]
    [InlineData(BackupCrypto.PieceSize * 2 + 7)]
    public void Encrypts_and_decrypts_any_size(int size)
    {
        var plain = RandomNumberGenerator.GetBytes(size);
        var sealedData = new MemoryStream();
        BackupCrypto.Encrypt(new MemoryStream(plain), sealedData, Password, FastIterations);

        var back = new MemoryStream();
        BackupCrypto.Decrypt(new MemoryStream(sealedData.ToArray()), back, Password);
        Assert.Equal(plain, back.ToArray());
    }

    [Fact]
    public void Wrong_password_is_refused()
    {
        var sealedData = new MemoryStream();
        BackupCrypto.Encrypt(new MemoryStream("hello"u8.ToArray()), sealedData, Password, FastIterations);
        var error = Assert.Throws<BackupPasswordException>(() =>
            BackupCrypto.Decrypt(new MemoryStream(sealedData.ToArray()), new MemoryStream(), "not the password"));
        Assert.Contains("password is wrong", error.Message);
    }

    [Fact]
    public void A_changed_byte_is_caught()
    {
        var sealedData = new MemoryStream();
        BackupCrypto.Encrypt(new MemoryStream(RandomNumberGenerator.GetBytes(5000)), sealedData, Password, FastIterations);
        var bytes = sealedData.ToArray();
        bytes[^100] ^= 1;
        Assert.Throws<BackupPasswordException>(() => BackupCrypto.Decrypt(new MemoryStream(bytes), new MemoryStream(), Password));
    }

    [Fact]
    public void A_file_cut_after_a_whole_piece_is_caught()
    {
        var sealedData = new MemoryStream();
        BackupCrypto.Encrypt(new MemoryStream(RandomNumberGenerator.GetBytes(BackupCrypto.PieceSize * 2 + 5)), sealedData, Password, FastIterations);
        var bytes = sealedData.ToArray();
        // Header (29) plus the first piece: its length (4), data and tag (16).
        var cut = bytes.AsSpan(0, 29 + 4 + BackupCrypto.PieceSize + 16).ToArray();
        Assert.Throws<BackupPasswordException>(() => BackupCrypto.Decrypt(new MemoryStream(cut), new MemoryStream(), Password));
    }

    [Fact]
    public void Other_files_are_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            BackupCrypto.Decrypt(new MemoryStream("not a backup at all, just text"u8.ToArray()), new MemoryStream(), Password));
    }

    // Making and restoring

    [Fact]
    public void A_backup_restores_everything_on_another_pc()
    {
        string id;
        using (var store = Open())
        {
            id = store.Save(Stored(Samples.Completed(Samples.NewInvoice()))).Id;
            store.SetSetting("documents", new DocumentSettings { InvoiceDueDays = 21 });
            MakeBackup(store);
        }
        var file = Assert.Single(Backups.List(Folder)).Path;
        Assert.Equal("WBAK"u8.ToArray(), File.ReadAllBytes(file)[..4]);

        // A new PC: empty data folder and a different key.
        var newPc = Path.Combine(root, "new-pc");
        var made = Backups.Stage(file, Password, newPc);
        Assert.Equal(Now, made);
        Assert.True(Backups.HasPending(newPc));

        byte[]? savedKey = null;
        Assert.True(Backups.ApplyPending(newPc, k => savedKey = k, Now));
        Assert.Equal(key, savedKey);
        Assert.False(Backups.HasPending(newPc));

        using var restored = new LocalStore(Path.Combine(newPc, "webignite.db"), new Vault(savedKey!));
        Assert.Equal("Karoo Coffee Roasters", restored.Get(id)!.Document.Customer.Name);
        Assert.Equal(21, restored.GetSetting<DocumentSettings>("documents")!.InvoiceDueDays);
    }

    [Fact]
    public void Restoring_keeps_the_current_data_aside()
    {
        using (var store = Open())
        {
            store.Save(Stored(Samples.Completed(Samples.NewInvoice())));
            MakeBackup(store);
        }
        File.WriteAllBytes(Path.Combine(Data, "vault.key"), [1, 2, 3]);

        Backups.Stage(Backups.List(Folder)[0].Path, Password, Data);
        Backups.ApplyPending(Data, _ => { }, Now);

        var aside = Assert.Single(Directory.GetDirectories(Data, "before-restore *"));
        Assert.True(File.Exists(Path.Combine(aside, "webignite.db")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(aside, "vault.key")));
    }

    [Fact]
    public void A_wrong_password_leaves_nothing_staged()
    {
        using (var store = Open())
            MakeBackup(store);
        Assert.Throws<BackupPasswordException>(() => Backups.Stage(Backups.List(Folder)[0].Path, "wrong password", Data));
        Assert.False(Backups.HasPending(Data));
        Assert.False(Directory.Exists(Path.Combine(Data, Backups.PendingFolder + ".tmp")));
    }

    [Fact]
    public void Nothing_pending_means_nothing_changes() =>
        Assert.False(Backups.ApplyPending(Data, _ => throw new InvalidOperationException(), Now));

    [Fact]
    public void Only_the_newest_fourteen_are_kept()
    {
        using var store = Open();
        for (var day = 0; day < 16; day++)
            MakeBackup(store, Now.AddDays(day));
        File.WriteAllText(Path.Combine(Folder, "my notes.txt"), "keep me");

        var kept = Backups.List(Folder);
        Assert.Equal(BackupRules.Keep, kept.Count);
        Assert.Equal(Now.AddDays(15), kept[0].Made);
        Assert.Equal(Now.AddDays(2), kept[^1].Made);
        Assert.True(File.Exists(Path.Combine(Folder, "my notes.txt")));
    }

    [Fact]
    public void The_key_check_catches_the_wrong_key()
    {
        using (var store = Open())
            store.SetSetting("documents", new DocumentSettings());
        using var wrong = Open(RandomNumberGenerator.GetBytes(32));
        Assert.ThrowsAny<CryptographicException>(wrong.Check);
    }

    // Rules

    [Fact]
    public void Due_once_a_day()
    {
        Assert.True(BackupRules.IsDue(new BackupSettings(), Now));
        Assert.False(BackupRules.IsDue(new BackupSettings { LastBackup = Now.AddHours(-23) }, Now));
        Assert.True(BackupRules.IsDue(new BackupSettings { LastBackup = Now.AddHours(-24) }, Now));
    }

    [Fact]
    public void File_names_read_back()
    {
        var name = BackupRules.FileName(Now);
        Assert.EndsWith(".wbak", name);
        Assert.Equal(Now, BackupRules.MadeAt(name));
        Assert.Null(BackupRules.MadeAt("holiday.wbak"));
    }

    [Theory]
    [InlineData("short", "short", "at least 10")]
    [InlineData("long enough one", "long enough two", "don't match")]
    [InlineData("long enough one", "long enough one", null)]
    public void Password_rules(string password, string repeated, string? problem)
    {
        var result = BackupRules.PasswordProblem(password, repeated);
        if (problem is null)
            Assert.Null(result);
        else
            Assert.Contains(problem, result);
    }

    [Fact]
    public void Status_text()
    {
        Assert.StartsWith("Backups are off", BackupRules.Status(new BackupSettings(), false, Now));
        Assert.StartsWith("Last backup today at", BackupRules.Status(new BackupSettings { LastBackup = Now.AddHours(-1) }, true, Now));
        Assert.StartsWith("The last backup failed",
            BackupRules.Status(new BackupSettings { LastBackup = Now.AddDays(-1), LastProblem = "Folder missing.", LastProblemAt = Now }, true, Now));
        Assert.True(BackupRules.Overdue(new BackupSettings { LastBackup = Now.AddDays(-3) }, true, Now));
        Assert.False(BackupRules.Overdue(new BackupSettings { LastBackup = Now.AddDays(-1) }, true, Now));
    }
}
