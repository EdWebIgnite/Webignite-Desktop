using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    private string DbPath => Path.Combine(folder, "test.db");
    private LocalStore Open() => new(DbPath, new Vault(key));

    private static StoredDocument Stored(Document doc) => new() { Id = StoredDocument.NewId(), Document = doc };

    [Fact]
    public void Saves_and_reads_back_a_document()
    {
        using var store = Open();
        var saved = store.Save(Stored(Samples.Completed(Samples.NewInvoice())));

        var read = store.Get(saved.Id)!;
        Assert.Equal("Karoo Coffee Roasters", read.Document.Customer.Name);
        Assert.Equal(450m, read.Document.Total);
        Assert.Equal(DocumentStatus.Draft, read.Status);
        Assert.NotEqual(default, read.Created);
    }

    [Fact]
    public void Survives_closing_and_opening()
    {
        string id;
        using (var store = Open())
            id = store.Save(Stored(Samples.Completed(Samples.NewInvoice()))).Id;
        using var again = Open();
        Assert.NotNull(again.Get(id));
    }

    [Fact]
    public void Updating_keeps_the_created_time()
    {
        using var store = Open();
        var first = store.Save(Stored(Samples.Completed(Samples.NewInvoice())));
        var second = store.Save(first with { Status = DocumentStatus.InXero, XeroNumber = "INV-0001" });

        Assert.Equal(first.Created, second.Created);
        Assert.Equal("INV-0001", store.Get(first.Id)!.XeroNumber);
        Assert.Single(store.List());
    }

    [Fact]
    public void Lists_by_kind()
    {
        using var store = Open();
        store.Save(Stored(Samples.Completed(Samples.NewInvoice())));
        store.Save(Stored(Samples.Completed(Samples.NewInvoice() with { Kind = DocumentKind.Quote })));

        Assert.Single(store.List(DocumentKind.Quote));
        Assert.Single(store.List(DocumentKind.Invoice));
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Customer_names_are_not_readable_in_the_file()
    {
        using (var store = Open())
            store.Save(Stored(Samples.Completed(Samples.NewInvoice())));
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(DbPath).Concat(File.Exists(DbPath + "-wal") ? File.ReadAllBytes(DbPath + "-wal") : []).ToArray();
        Assert.DoesNotContain("Karoo", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Wrong_key_cannot_read()
    {
        string id;
        using (var store = Open())
            id = store.Save(Stored(Samples.Completed(Samples.NewInvoice()))).Id;
        using var other = new LocalStore(DbPath, new Vault(RandomNumberGenerator.GetBytes(32)));
        Assert.ThrowsAny<CryptographicException>(() => other.Get(id));
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var store = Open();
        Assert.Null(store.GetSetting<DocumentSettings>("documents"));
        store.SetSetting("documents", new DocumentSettings { InvoiceDueDays = 14 });
        Assert.Equal(14, store.GetSetting<DocumentSettings>("documents")!.InvoiceDueDays);
    }

    [Fact]
    public void Vault_rejects_data_moved_to_another_record()
    {
        var vault = new Vault(key);
        var sealedData = vault.Encrypt("secret"u8.ToArray(), "document:a");
        Assert.Equal("secret"u8.ToArray(), vault.Decrypt(sealedData, "document:a"));
        Assert.ThrowsAny<CryptographicException>(() => vault.Decrypt(sealedData, "document:b"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
