using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Webignite.Core.Finance;
using Webignite.Core.Renewals;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class RenewalTests : IDisposable
{
    private static readonly DateOnly Today = Samples.Today; // 31 Oct 2026
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private static readonly HashSet<string> None = [];

    private static Renewal Domain(DateOnly renewsOn, decimal? price = 250m) => new()
    {
        Id = "d1",
        Name = "karoocoffee.co.za",
        ClientName = "Karoo Coffee Roasters",
        Provider = "domains.co.za",
        RenewsOn = renewsOn,
        Cost = 99m,
        Price = price,
    };

    [Theory]
    [InlineData(2026, 12, 15, RenewalState.Fine)]
    [InlineData(2026, 11, 30, RenewalState.DueSoon)]
    [InlineData(2026, 10, 31, RenewalState.DueSoon)]
    [InlineData(2026, 10, 30, RenewalState.Overdue)]
    public void State_follows_the_reminder_window(int y, int m, int d, RenewalState expected) =>
        Assert.Equal(expected, RenewalRules.State(Domain(new DateOnly(y, m, d)), Today));

    [Fact]
    public void Reminder_is_raised_once_inside_the_window()
    {
        var task = Assert.Single(RenewalRules.Reminders([Domain(new DateOnly(2026, 11, 14)), Domain(new DateOnly(2027, 1, 1)) with { Id = "d2" }],
            None, Today, DateTimeOffset.UnixEpoch));
        Assert.Equal("Renew karoocoffee.co.za for Karoo Coffee Roasters", task.Title);
        Assert.Equal(new DateOnly(2026, 11, 14), task.Due);
        Assert.Empty(RenewalRules.Reminders([Domain(new DateOnly(2026, 11, 14))], new HashSet<string> { task.AutoKey! }, Today, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Invoice_covers_the_period_and_is_only_for_paid_renewals()
    {
        var (key, invoice) = Assert.Single(RenewalRules.InvoicesDue([Domain(new DateOnly(2026, 11, 14))], None, Samples.Settings, Today));
        Assert.Equal("renewal-invoice:d1:2026-11-14", key);
        Assert.Equal("Domain renewal: karoocoffee.co.za, 14 Nov 2026 to 13 Nov 2027", invoice.Lines[0].Description);
        Assert.Equal(250m, invoice.Total);
        Assert.Equal(Today, invoice.Date);
        Assert.Empty(DocumentValidator.Validate(invoice));

        Assert.Empty(RenewalRules.InvoicesDue([Domain(new DateOnly(2026, 11, 14), price: null)], None, Samples.Settings, Today));
        Assert.Empty(RenewalRules.InvoicesDue([Domain(new DateOnly(2026, 11, 14)) with { ClientName = null }], None, Samples.Settings, Today));
    }

    [Fact]
    public void Renewed_moves_on_a_period_and_books_the_cost()
    {
        var (next, expense) = RenewalRules.Renewed(Domain(new DateOnly(2026, 11, 14)), Today, DateTimeOffset.UnixEpoch);
        Assert.Equal(new DateOnly(2027, 11, 14), next.RenewsOn);
        Assert.NotNull(expense);
        Assert.Equal((EntryKind.Expense, 99m, "domains.co.za", "Domains", Today), (expense.Kind, expense.Amount, expense.Party, expense.Category, expense.Date));
        Assert.Null(RenewalRules.Renewed(Domain(Today) with { Cost = null }, Today, DateTimeOffset.UnixEpoch).Expense);
    }

    [Fact]
    public void Check_wants_a_client_when_there_is_a_charge()
    {
        Assert.Empty(RenewalRules.Check(Domain(Today)));
        Assert.Equal(["Choose the client to charge, or leave the charge empty."], RenewalRules.Check(Domain(Today) with { ClientName = "" }));
    }

    [Fact]
    public void Store_raises_reminder_and_invoice_once_then_marks_renewed()
    {
        using var store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SaveRenewal(Domain(new DateOnly(2026, 11, 14)));
        Assert.Equal((1, 1), store.RaiseRenewals(Samples.Settings, Today));
        Assert.Equal((0, 0), store.RaiseRenewals(Samples.Settings, Today));
        Assert.Single(store.List(DocumentKind.Invoice));

        var renewed = store.MarkRenewed(store.ListRenewals()[0], Today);
        Assert.Equal(new DateOnly(2027, 11, 14), renewed.RenewsOn);
        Assert.Equal(99m, Assert.Single(store.ListEntries(Today, Today)).Amount);
        Assert.Equal((0, 0), store.RaiseRenewals(Samples.Settings, Today));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
