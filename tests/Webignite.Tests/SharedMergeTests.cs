using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Sync;

namespace Webignite.Tests;

/// <summary>Two copies of a quote, client or contact made into one when the two PCs sync.</summary>
public sealed class SharedMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static StoredDocument Doc() => new() { Id = "q1", Document = Samples.Completed(Samples.NewInvoice() with { Kind = DocumentKind.Quote }) };

    [Fact]
    public void The_later_copy_wins_and_a_deletion_stands_unless_changed_after()
    {
        var a = new Client { Id = "c", Name = "Old name" };
        var b = a with { Name = "New name" };
        Assert.Equal("New name", SharedMerge.Merge(new Shared<Client>(a, T0), new Shared<Client>(b, T0.AddMinutes(1)), (o, n) => n).Value!.Name);
        Assert.Equal("New name", SharedMerge.Merge(new Shared<Client>(b, T0.AddMinutes(1)), new Shared<Client>(a, T0), (o, n) => n).Value!.Name);

        var gone = new Shared<Client>(null, T0.AddMinutes(2), T0.AddMinutes(2));
        Assert.NotNull(SharedMerge.Merge(new Shared<Client>(b, T0.AddMinutes(1)), gone, (o, n) => n).Deleted);
        Assert.Equal("New name", SharedMerge.Merge(gone, new Shared<Client>(b, T0.AddMinutes(3)), (o, n) => n).Value!.Name);
    }

    [Fact]
    public void A_newer_copy_of_a_quote_never_undoes_what_happened_on_the_other_pc()
    {
        var older = Doc() with
        {
            Delivery = new Delivery { Approved = T0, Emailed = T0.AddMinutes(1), EmailedTo = "info@karoo.co.za", ChaseCount = 2 },
            Settled = new SettledState { On = T0.AddDays(3), As = Settlement.Paid },
            Xero = new XeroLink { Id = "x1", State = Webignite.Core.Xero.XeroWriteState.Verified },
            CreditNotes = [new CreditNoteLink { Id = "cn1", Amount = 100m }],
        };
        var newer = Doc() with { Document = Doc().Document with { Reference = "Changed later" } };

        var merged = SharedMerge.Document(older, newer);
        Assert.Equal("Changed later", merged.Document.Reference);
        Assert.Equal(T0, merged.Delivery.Approved);
        Assert.Equal("info@karoo.co.za", merged.Delivery.EmailedTo);
        Assert.Equal(2, merged.Delivery.ChaseCount);
        Assert.Equal(Settlement.Paid, merged.Settled!.As);
        Assert.Equal("x1", merged.Xero.Id);
        Assert.Single(merged.CreditNotes);
    }

    [Fact]
    public void A_clients_folder_and_files_stay_as_they_are_on_this_pc()
    {
        var local = new Client { Id = "c", Name = "Karoo", Folder = @"D:\Work\Karoo" };
        var remote = local with { Name = "Karoo Coffee", Folder = @"C:\Clients\Karoo" };
        var merged = SharedMerge.Client(local, local, remote);
        Assert.Equal("Karoo Coffee", merged.Name);
        Assert.Equal(@"D:\Work\Karoo", merged.Folder);
    }
}
