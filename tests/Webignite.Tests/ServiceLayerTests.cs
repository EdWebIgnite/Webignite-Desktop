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
