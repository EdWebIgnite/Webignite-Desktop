using System.Security.Cryptography;
using Webignite.Core.Documents;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Bringing in old quotes and invoices, taking them out again, and Claude or ChatGPT only preparing them.</summary>
public sealed class DocumentImportStoreTests : IDisposable
{
    private const string Csv = "Type,Number,Client,Date,Total,Status\nInvoice,INV-0050,G3,2025-03-01,1700,Paid\nQuote,QU-0901,Modics,2025-02-01,4500,Accepted\n";

    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;

    public DocumentImportStoreTests() => store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));

    [Fact]
    public void Brought_in_once_only_and_Undo_takes_out_just_those()
    {
        var service = new DocumentService(store);
        var mine = store.Save(new StoredDocument
        {
            Id = StoredDocument.NewId(),
            Document = new Document { Kind = DocumentKind.Invoice, Origin = DocumentOrigin.New, Customer = new Customer { Name = "G3" }, Lines = [] },
        });

        var plan = DocumentImport.Plan(Csv, null, service.List(), DateTimeOffset.UtcNow);
        var ids = service.Import(plan.Documents);
        Assert.Equal(2, ids.Count);
        Assert.Empty(service.Import(plan.Documents));
        Assert.Equal(3, service.List().Count);

        Assert.Equal(2, service.Unimport([.. ids, mine.Id]));
        Assert.Equal([mine.Id], service.List().Select(d => d.Id));
    }

    [Fact]
    public void An_assistant_only_prepares_an_import_for_Eddie_to_bring_in()
    {
        var assistant = new AssistantDocuments(store);

        assistant.PrepareImport(Csv, null, DocumentSource.Claude);

        Assert.Empty(store.List());
        var waiting = store.GetSetting<PendingImport>(PendingImport.SettingName);
        Assert.Equal("Claude", waiting?.From);
        Assert.Equal(Csv, waiting?.Csv);
    }

    [Fact]
    public void An_assistant_import_with_nothing_new_leaves_nothing_waiting()
    {
        var service = new DocumentService(store);
        service.Import(DocumentImport.Plan(Csv, null, [], DateTimeOffset.UtcNow).Documents);

        new AssistantDocuments(store).PrepareImport(Csv, null, DocumentSource.ChatGpt);

        Assert.Null(store.GetSetting<PendingImport>(PendingImport.SettingName));
        Assert.Throws<ArgumentException>(() => new AssistantDocuments(store).PrepareImport(" ", null, DocumentSource.ChatGpt));
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
