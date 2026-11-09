using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Data;
using Webignite.Services;
using Webignite.Xero;

namespace Webignite.Tests;

/// <summary>Bills from suppliers, read from Xero so Money and Overview show what Webignite owes.</summary>
public sealed class BillsTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 9);
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-bills-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static XeroBill Bill(string supplier, decimal due, int inDays, string status = "AUTHORISED") => new()
    {
        Id = supplier, Supplier = supplier, Status = status, Total = due, AmountDue = due, Due = Today.AddDays(inDays),
    };

    [Fact]
    public void What_is_owed_overdue_and_due_this_week()
    {
        XeroBill[] bills = [Bill("xneelo", 1899m, -3), Bill("Adobe", 1150m, 5), Bill("Takealot", 349.99m, 20), Bill("Printer", 500m, 2, "DRAFT")];
        Assert.Equal(3398.99m, Bills.Owed(bills));
        Assert.Equal(1899m, Bills.Overdue(bills, Today));
        Assert.Equal(3049m, Bills.DueWithin(bills, Today, 7));
        Assert.Equal(["xneelo", "Adobe", "Takealot", "Printer"], Bills.InOrder(bills).Select(b => b.Supplier));
        Assert.Equal($"{Money.Rand(3398.99m)} owed on 3 bills, {Money.Rand(1899m)} overdue, 1 waiting to be approved in Xero.", Bills.Summary(bills, Today));
        Assert.Equal("No bills to pay in Xero.", Bills.Summary([], Today));
    }

    [Fact]
    public void Xeros_bill_is_read()
    {
        var bill = XeroApi.ToBill(JsonNode.Parse("""
            {"InvoiceID":"b1","InvoiceNumber":"INV-7781","Type":"ACCPAY","Status":"AUTHORISED","Reference":"Server November",
             "Contact":{"Name":"xneelo"},"DateString":"2026-11-01T00:00:00","DueDateString":"2026-11-08T00:00:00","Total":1899,"AmountDue":1899}
            """)!.AsObject());
        Assert.Equal(("b1", "INV-7781", "xneelo", "Server November"), (bill.Id, bill.Number, bill.Supplier, bill.Reference));
        Assert.Equal(new DateOnly(2026, 11, 8), bill.Due);
        Assert.True(Bills.IsOverdue(bill, Today));
    }

    [Fact]
    public async Task The_watch_keeps_what_xero_said_and_says_when_it_cant()
    {
        using var store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        IReadOnlyList<XeroBill> bills = [Bill("xneelo", 1899m, -3)];
        var watch = new XeroBillWatch(store, _ => Task.FromResult(bills));
        Assert.True(watch.Due(DateTimeOffset.UtcNow));
        Assert.Null(await watch.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal("xneelo", Assert.Single(watch.State.Open).Supplier);
        Assert.NotNull(watch.State.Read);
        Assert.False(watch.Due(DateTimeOffset.UtcNow));

        var failing = new XeroBillWatch(store, _ => Task.FromException<IReadOnlyList<XeroBill>>(new XeroException("Xero is down")));
        Assert.Equal("Couldn't read the bills from Xero: Xero is down", await failing.RunAsync(TestContext.Current.CancellationToken));
        Assert.Single(failing.State.Open); // what was read before stays

        Assert.Equal("Connect Xero in Settings first.", await new XeroBillWatch(store, () => null).RunAsync(TestContext.Current.CancellationToken));
    }
}
