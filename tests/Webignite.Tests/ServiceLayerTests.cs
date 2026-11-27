using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Core.Xero;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Pages read and change quotes, invoices and clients through these, so one change can't undo another.</summary>
public sealed class ServiceLayerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-services-").FullName;
    private readonly LocalStore store;

    public ServiceLayerTests() => store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));

    public void Dispose()
    {
        store.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void A_bank_statement_records_what_was_confirmed_and_shows_only_new_lines_next_time()
    {
        var documents = new DocumentService(store);
        var import = new BankImport(store, documents);
        var now = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.FromHours(2));
        store.Save(Samples.InXero("0104", 2800m).WithXero(x => x with { Status = "AUTHORISED" }));
        var bill = store.SaveSupplierBill(new Webignite.Core.Finance.SupplierBill { Id = "b1", Supplier = "xneelo (Pty) Ltd", Date = new DateOnly(2026, 11, 1), Total = 1899m });
        var csv = "Date,Description,Amount\n2026/11/03,FNB APP PAYMENT INV-0104,2800.00\n2026/11/04,DEBIT ORDER XNEELO,-1899.00\n2026/11/05,ADOBE SYSTEMS 1234,-1150.00\n";
        var statement = Webignite.Core.Finance.BankStatements.Read("s.csv", csv);
        var settings = new Webignite.Core.Finance.BankSettings();

        var lines = import.Prepare(statement, settings);
        Assert.Equal([Webignite.Core.Finance.BankMatchKind.NewExpense, Webignite.Core.Finance.BankMatchKind.Bill, Webignite.Core.Finance.BankMatchKind.Invoice],
            lines.Select(l => l.Match.Kind));
        foreach (var (line, match) in lines)
            import.Apply(line, match, "Software and subscriptions", "Adobe", now);

        var entries = store.ListEntries(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        Assert.Contains(entries, e => e.Kind == Webignite.Core.Finance.EntryKind.Received && e.Amount == 2800m && e.Note == "INV-0104");
        Assert.Contains(entries, e => e.BillId == bill.Id && e.Date == new DateOnly(2026, 11, 4));
        Assert.Contains(entries, e => e.Category == "Software and subscriptions" && e.Party == "Adobe" && e.Amount == 1150m);
        Assert.NotNull(store.GetSupplierBill("b1")!.Paid);

        settings = settings.WithSeen(lines.Select(l => l.Line.Key));
        Assert.Empty(import.Prepare(statement, settings));
    }

    [Fact]
    public void A_clients_old_projects_move_into_the_Planner_once_and_time_keeps_seeing_them()
    {
        var clients = new ClientService(store);
        var projects = new ProjectService(store, () => "me", attachmentsFolder: Path.Combine(folder, "attachments"));
        var studio = clients.Add("Studio Nine");
        clients.Change(studio.Id, c => c with { Projects = [new ClientProject { Id = "p1", Name = "New website", HourlyRate = 700m }] });

        Assert.Equal(1, projects.MergeClientProjects(clients.List()));
        Assert.Equal(0, projects.MergeClientProjects(clients.List()));
        var moved = projects.Get("p1")!;
        Assert.Equal(studio.Id, moved.ClientId);
        Assert.Equal(700m, moved.HourlyRate);

        projects.Change("p1", p => p with { For = "Karoo Coffee" });
        var forTime = clients.ForTime().Single(c => c.Id == studio.Id);
        Assert.Equal("New website for Karoo Coffee", Assert.Single(forTime.Projects).Name);
    }

    [Fact]
    public void Saving_the_client_details_keeps_notes_and_projects_added_meanwhile()
    {
        var clients = new ClientService(store);
        var karoo = clients.Add("Karoo Coffee Roasters");
        var openOnThePage = karoo; // the page's copy, from before the note
        clients.Change(karoo.Id, c => ClientRecords.AddNote(c, "Wants the shop in phase two", Samples.Today));
        clients.Change(karoo.Id, c => c with { Projects = [new ClientProject { Id = "p1", Name = "New website" }] });

        var (saved, problems) = clients.SaveDetails(openOnThePage with { Email = "thandi@karoocoffee.co.za" });

        Assert.Empty(problems);
        Assert.Equal("thandi@karoocoffee.co.za", saved!.Email);
        Assert.Single(saved.Log);
        Assert.Single(saved.Projects);
    }

    [Fact]
    public void Client_details_with_a_problem_change_nothing()
    {
        var clients = new ClientService(store);
        var karoo = clients.Add("Karoo Coffee Roasters");
        clients.Add("Fynbos Physio");
        var (saved, problems) = clients.SaveDetails(karoo with { Name = "fynbos  physio" });
        Assert.Null(saved);
        Assert.NotEmpty(problems);
        Assert.Equal("Karoo Coffee Roasters", clients.Get(karoo.Id)!.Name);
    }

    [Fact]
    public void A_draft_saves_while_on_this_pc_and_not_once_it_is_in_xero()
    {
        var documents = new DocumentService(store);
        var draft = documents.Create(Samples.Completed(Samples.NewInvoice()));
        var typed = draft.Document with { Reference = "Website, November" };
        Assert.Equal("Website, November", documents.SaveDraft(draft.Id, typed).Document.Reference);

        documents.Change(draft.Id, d => d with { Xero = d.Xero with { State = XeroWriteState.Verified, Id = "x1" } });
        var refused = documents.SaveDraft(draft.Id, typed with { Reference = "Changed after" });
        Assert.Equal("Website, November", refused.Document.Reference);
        Assert.Equal(XeroWriteState.Verified, refused.Xero.State);
    }

    [Fact]
    public void A_new_draft_is_saved_under_the_id_it_was_given()
    {
        var documents = new DocumentService(store);
        var saved = documents.SaveDraft("new-1", Samples.Completed(Samples.NewInvoice()));
        Assert.Equal("new-1", saved.Id);
        Assert.NotNull(documents.Get("new-1"));
    }

    [Fact]
    public void Only_assistant_work_that_never_reached_xero_is_rejected_and_undo_brings_it_back()
    {
        var documents = new DocumentService(store);
        var fromClaude = documents.Put(Samples.Stored() with { Status = DocumentStatus.AwaitingApproval, Source = DocumentSource.Claude });
        var gone = documents.Reject(new WaitingItem(fromClaude, WaitingKind.FromAssistant, ""));
        Assert.NotNull(gone);
        Assert.Null(documents.Get(fromClaude.Id));
        documents.Restore(gone);
        Assert.NotNull(documents.Get(fromClaude.Id));

        // It went to Xero from another page meanwhile: no longer thrown away from here.
        documents.Change(fromClaude.Id, d => d with { Xero = d.Xero with { Id = "x9" } });
        Assert.Null(documents.Reject(new WaitingItem(fromClaude, WaitingKind.FromAssistant, "")));
        Assert.NotNull(documents.Get(fromClaude.Id));
    }
}
