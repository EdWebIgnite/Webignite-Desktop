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
    public void An_assistant_changes_a_document_that_has_not_reached_Xero_in_place()
    {
        var made = documents.Draft("quote", "Karoo Coffee Roasters", [new("Home page design", 1, 4500m, "Design"), new("Hosting for a year", 1, 1800m)],
            DocumentSource.Claude, reference: "Website", notes: "Valid for 30 days.");
        var changed = documents.Change(made.Id, DocumentSource.ChatGpt,
            [new("Home page design", 1, 5200m, "Design"), new("Hosting for a year", 1, 1800m), new("Contact form", 1, 950m)], discountPercent: 10);

        Assert.Equal(made.Id, changed.Id);
        var stored = Assert.Single(store.List(null));
        Assert.Equal([5200m, 1800m, 950m], stored.Document.Lines.Select(l => l.UnitPrice ?? 0));
        Assert.Equal(10m, stored.Document.DocumentDiscountPercent);
        // The totals follow the new lines and the discount.
        Assert.Equal(7950m, stored.Document.Subtotal);
        Assert.Equal(7155m, stored.Document.Total);
        Assert.Equal(Money.Rand(7155m), changed.Total);
        // What was left out stays as it was, and so does who it is for.
        Assert.Equal("Website", stored.Document.Reference);
        Assert.Equal("Valid for 30 days.", stored.Document.Notes);
        Assert.Equal("Karoo Coffee Roasters", stored.Document.Customer.Name);
        Assert.Equal(DocumentSource.ChatGpt, stored.Source);
        Assert.Contains("ChatGPT changed this quote", Approvals.Why(stored)!.Reason, StringComparison.Ordinal);
        Assert.True(documents.List().Single().CanChange);

        // An empty string clears, and 0 takes the discount off.
        documents.Change(made.Id, DocumentSource.Claude, reference: "", discountPercent: 0);
        stored = store.Get(made.Id)!;
        Assert.Null(stored.Document.Reference);
        Assert.Null(stored.Document.DocumentDiscountPercent);
        Assert.Equal(3, stored.Document.Lines.Count);
        Assert.Equal(7950m, stored.Document.Total);
    }

    [Fact]
    public void An_assistant_can_change_Eddies_own_draft_and_it_then_waits_to_be_checked()
    {
        var mine = store.Save(Doc(DocumentKind.Quote));
        Assert.Null(Approvals.Why(mine));
        documents.Change(mine.Id, DocumentSource.Claude, notes: "Half up front.");
        var stored = store.Get(mine.Id)!;
        Assert.Equal("Half up front.", stored.Document.Notes);
        Assert.Equal(WaitingKind.FromAssistant, Approvals.Why(stored)!.Kind);
        Assert.Contains("Claude changed this quote", Approvals.Why(stored)!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assistant_changes_a_draft_in_Xero_and_Xero_keeps_its_copy_until_Eddie_updates_it()
    {
        var draft = store.Save(Doc(DocumentKind.Quote).WithXero(x => x with { State = XeroWriteState.Verified, Id = "x1", Number = "QU-0042", Status = "DRAFT" })
            with { Status = DocumentStatus.InXero });
        Assert.True(documents.List().Single().CanChange);
        // Compared as JSON: a document read back from the store holds its lists in another kind of list.
        static string Json(Document? d) => System.Text.Json.JsonSerializer.Serialize(d, DocumentJson.Options);
        var xeros = Json(store.Get(draft.Id)!.Document);

        var changed = documents.Change(draft.Id, DocumentSource.ChatGpt, [new("Home page design", 1, 5200m, "Design")], notes: "Half up front.");
        Assert.Contains("Update draft in Xero", changed.Message, StringComparison.Ordinal);

        var stored = store.Get(draft.Id)!;
        // Changed here, beside Xero's copy, exactly as when Eddie clicks Change this draft.
        Assert.Equal(XeroWriteState.Editing, stored.Xero.State);
        Assert.Equal(xeros, Json(stored.BeforeChange));
        Assert.Equal("Half up front.", stored.Document.Notes);
        Assert.Equal(5200m, stored.Document.Total);
        Assert.Equal(DocumentStatus.InXero, stored.Status);
        Assert.NotNull(stored.ChangedByAssistant);
        var why = Approvals.Why(stored)!;
        Assert.Equal(WaitingKind.XeroDraft, why.Kind);
        Assert.Contains("ChatGPT changed this draft quote", why.Reason, StringComparison.Ordinal);
        Assert.False(Approvals.CanReject(why)); // it is in Xero: Discard changes, not Reject

        // A second change keeps Xero's copy as it was, not the first change.
        documents.Change(draft.Id, DocumentSource.Claude, reference: "Website");
        stored = store.Get(draft.Id)!;
        Assert.Equal(xeros, Json(stored.BeforeChange));
        Assert.Equal("Website", stored.Document.Reference);
        Assert.Equal("Half up front.", stored.Document.Notes);

        // Discard changes puts Xero's copy back, and nothing says an assistant changed it any more.
        var discarded = XeroEdits.StopChanging(stored)!;
        Assert.Equal(xeros, Json(discarded.Document));
        Assert.Null(discarded.ChangedByAssistant);
        Assert.Equal(XeroWriteState.Verified, discarded.Xero.State);
    }

    [Fact]
    public void An_assistant_doesnt_change_a_draft_Eddie_is_changing_or_one_made_in_Xero()
    {
        var eddies = store.Save(XeroEdits.StartChanging(Doc(DocumentKind.Quote).WithXero(x => x with { State = XeroWriteState.Verified, Id = "x1", Status = "DRAFT" }))!);
        var refused = Assert.Throws<ArgumentException>(() => documents.Change(eddies.Id, DocumentSource.Claude, notes: "Changed"));
        Assert.Contains("Eddie is changing", refused.Message, StringComparison.Ordinal);

        var fromXero = store.Save(Doc(DocumentKind.Quote).WithXero(x => x with { State = XeroWriteState.Verified, Id = "x2", Status = "DRAFT" })
            with { Document = Samples.Completed(Samples.NewInvoice() with { Kind = DocumentKind.Quote, Origin = DocumentOrigin.Xero }) });
        Assert.Throws<ArgumentException>(() => documents.Change(fromXero.Id, DocumentSource.Claude, notes: "Changed"));
        Assert.Null(store.Get(fromXero.Id)!.ChangedByAssistant);
    }

    [Fact]
    public void An_assistant_cant_change_what_is_approved_or_emailed_or_ask_for_nothing()
    {
        var inXero = store.Save(Doc().WithXero(x => x with { State = XeroWriteState.Verified, Id = "x1" })
            with { Status = DocumentStatus.InXero, Delivery = new() { Emailed = DateTimeOffset.UtcNow } });
        var refused = Assert.Throws<ArgumentException>(() => documents.Change(inXero.Id, DocumentSource.Claude, notes: "Changed"));
        Assert.Contains("already in Xero", refused.Message, StringComparison.Ordinal);
        Assert.Null(store.Get(inXero.Id)!.Document.Notes);
        Assert.Null(store.Get(inXero.Id)!.ChangedByAssistant);
        Assert.False(documents.List().Single().CanChange);

        var local = store.Save(Doc(DocumentKind.Quote));
        Assert.Throws<ArgumentException>(() => documents.Change(local.Id, DocumentSource.Claude));
        Assert.Throws<ArgumentException>(() => documents.Change(local.Id, DocumentSource.Claude, lines: []));
        Assert.Throws<ArgumentException>(() => documents.Change(local.Id, DocumentSource.Claude, lines: [new("Refund", 1, -100m)]));
        Assert.Throws<ArgumentException>(() => documents.Change(local.Id, DocumentSource.Claude, discountPercent: 10, discountAmount: 100));
        Assert.Throws<ArgumentException>(() => documents.Change("nope", DocumentSource.Claude, notes: "x"));
    }

    [Fact]
    public void Only_what_never_reached_Xero_can_be_deleted_here()
    {
        var service = new Webignite.Services.DocumentService(store);
        var local = store.Save(Doc(DocumentKind.Quote));
        var inXero = store.Save(Doc().WithXero(x => x with { State = XeroWriteState.Verified, Id = "x1" }) with { Status = DocumentStatus.InXero });
        Assert.True(Approvals.CanDelete(local));
        Assert.False(Approvals.CanDelete(inXero));

        Assert.Null(service.Delete(inXero.Id));
        Assert.NotNull(store.Get(inXero.Id));
        var gone = service.Delete(local.Id);
        Assert.NotNull(gone);
        Assert.Null(store.Get(local.Id));
        // Undo puts it back as it was.
        service.Restore(gone!);
        Assert.Equal(local.Id, store.Get(local.Id)!.Id);
        Assert.Null(service.Delete("nope"));
    }

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
        Assert.Equal("waiting for Eddie under To approve, not in Xero yet", listed.Status);
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
