using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class ApprovalTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;
    private readonly AssistantDocuments documents;

    public ApprovalTests()
    {
        store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        documents = new AssistantDocuments(store);
    }

    private static StoredDocument Doc(DocumentKind kind = DocumentKind.Invoice) => new()
    {
        Id = StoredDocument.NewId(),
        Document = Samples.Completed(Samples.NewInvoice() with { Kind = kind }),
    };

    [Fact]
    public void Something_settled_or_taken_off_by_hand_leaves_the_list_and_only_what_is_in_Xero_can_be_taken_off()
    {
        var draft = Doc(DocumentKind.Quote).WithXero(x => x with { State = XeroWriteState.Mismatch, Id = "q9" }) with { Status = DocumentStatus.InXero };
        var item = Assert.Single(Approvals.Waiting([draft]));
        Assert.True(Approvals.CanRemove(item));

        var deleted = draft with { Settled = new SettledState { On = DateTimeOffset.UtcNow, As = Settlement.Deleted } };
        var off = draft with { OffApprovals = DateTimeOffset.UtcNow };
        Assert.Empty(Approvals.Waiting([deleted, off]));

        var fromClaude = Doc() with { Status = DocumentStatus.AwaitingApproval, Source = DocumentSource.Claude };
        Assert.False(Approvals.CanRemove(Assert.Single(Approvals.Waiting([fromClaude]))));
    }

    [Fact]
    public void The_list_holds_what_needs_Eddie_most_urgent_first()
    {
        var mine = Doc();
        var fromClaude = Doc() with { Status = DocumentStatus.AwaitingApproval, Source = DocumentSource.Claude };
        var draft = Doc().WithXero(x => x with { State = XeroWriteState.Verified, Id = "x1" }) with { Status = DocumentStatus.InXero };
        var approved = draft with { Id = "a", Delivery = draft.Delivery with { Approved = DateTimeOffset.UtcNow } };
        var emailed = approved with { Id = "e", Delivery = approved.Delivery with { Emailed = DateTimeOffset.UtcNow } };
        var broken = Doc().WithXero(x => x with { State = XeroWriteState.Uncertain });
        var quote = Doc(DocumentKind.Quote).WithXero(x => x with { State = XeroWriteState.Verified, Id = "q1" });

        var waiting = Approvals.Waiting([mine, fromClaude, draft, approved, emailed, broken, quote]);

        Assert.Equal([WaitingKind.XeroProblem, WaitingKind.FromAssistant, WaitingKind.XeroDraft, WaitingKind.XeroDraft, WaitingKind.NotEmailed],
            waiting.Select(w => w.Kind));
        Assert.DoesNotContain(waiting, w => w.Stored == mine || w.Stored == emailed);
        Assert.Contains("Claude prepared this invoice", waiting[1].Reason);
        Assert.Contains("quote", Assert.Single(waiting, w => w.Stored == quote).Reason);
    }

    [Fact]
    public void A_draft_being_changed_waits_and_drafts_made_in_xero_stay_there()
    {
        var draft = Doc().WithXero(x => x with { State = XeroWriteState.Verified, Id = "x1" }) with { Status = DocumentStatus.InXero };
        var changing = XeroEdits.StartChanging(draft)!;
        Assert.Contains("Update draft in Xero", Approvals.Why(changing)!.Reason, StringComparison.Ordinal);

        var madeInXero = draft with { Document = draft.Document with { Origin = DocumentOrigin.Xero } };
        Assert.Null(Approvals.Why(madeInXero));
        Assert.Null(Approvals.Why(madeInXero with { Delivery = madeInXero.Delivery with { Approved = DateTimeOffset.UtcNow } }));
        Assert.Null(XeroEdits.StartChanging(madeInXero));
    }

    [Fact]
    public void An_assistant_prepares_a_document_that_waits_for_Eddie()
    {
        store.SaveClient(new Client { Id = "c", Name = "Karoo Coffee Roasters", Email = "accounts@karoocoffee.co.za; thandi@karoocoffee.co.za" });

        var prepared = documents.Draft("Invoice", " karoo coffee roasters ",
            [new("Website hosting, November", 1, 450m), new("Content updates (hours)", 3.5m, 650m), new("Thank you!")],
            DocumentSource.ChatGpt, reference: "Website November");

        Assert.Equal(Money.Rand(2725m), prepared.Total);
        Assert.Empty(prepared.StillNeeded);
        Assert.Contains("To approve", prepared.Message);
        var stored = store.Get(prepared.Id)!;
        Assert.Equal(DocumentStatus.AwaitingApproval, stored.Status);
        Assert.Equal(DocumentSource.ChatGpt, stored.Source);
        Assert.Equal(XeroWriteState.None, stored.Xero.State);
        Assert.Equal("Karoo Coffee Roasters", stored.Document.Customer.Name);
        Assert.Equal("accounts@karoocoffee.co.za", stored.Document.Customer.Email);
        Assert.Null(stored.Document.Lines[2].Quantity);
        Assert.NotNull(stored.Document.DueOrExpiryDate);
        Assert.Equal(WaitingKind.FromAssistant, Approvals.Why(stored)!.Kind);

        var listed = Assert.Single(documents.List("invoices", "karoo"));
        Assert.Equal("waiting for Eddie under To approve", listed.Status);
        Assert.Contains("Content updates (hours): 3.5 x " + Money.Rand(650m), listed.Lines);
    }

    [Theory]
    [InlineData("receipt", "Customer", "Line", 1)]
    [InlineData("quote", " ", "Line", 1)]
    [InlineData("quote", "Customer", " ", 1)]
    [InlineData("quote", "Customer", "Line", -5)]
    public void Bad_requests_are_turned_down_with_a_reason(string kind, string customer, string line, decimal price)
    {
        Assert.Throws<ArgumentException>(() => documents.Draft(kind, customer, [new(line, 1, price)], DocumentSource.Claude));
        Assert.Empty(store.List());
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
