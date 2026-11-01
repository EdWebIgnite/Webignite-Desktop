using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Webignite.Core.Finance;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class FinanceTests : IDisposable
{
    private static readonly DateOnly Today = Samples.Today; // 31 Oct 2026
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;

    private static MoneyEntry In(DateOnly date, decimal amount, string? from = null) =>
        new() { Id = MoneyEntry.NewId(), Kind = EntryKind.Received, Date = date, Amount = amount, Party = from };

    private static MoneyEntry Out(DateOnly date, decimal amount, string? category = null) =>
        new() { Id = MoneyEntry.NewId(), Kind = EntryKind.Expense, Date = date, Amount = amount, Party = "Supplier", Category = category };

    private static RecurringExpense Server(DateOnly starts, int day = 1) =>
        new() { Id = "r1", Name = "xneelo server", Amount = 1899m, DayOfMonth = day, Starts = starts, Category = "Hosting and servers" };

    [Fact]
    public void Months_add_up_money_in_and_out()
    {
        var months = Ledger.Months(
            [In(new(2026, 10, 3), 2800m), In(new(2026, 10, 20), 450m), Out(new(2026, 10, 1), 1899m), In(new(2026, 9, 30), 1000m)],
            Today, 3);

        Assert.Equal([(8, 0m, 0m), (9, 1000m, 0m), (10, 3250m, 1899m)], months.Select(m => (m.Month, m.Received, m.Expenses)));
        Assert.Equal(1351m, months[^1].LeftOver);
    }

    [Fact]
    public void Months_cross_the_new_year()
    {
        var months = Ledger.Months([], new DateOnly(2027, 1, 15), 3);
        Assert.Equal([new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1)], months.Select(m => m.First));
    }

    [Theory]
    [InlineData(2026, 10, 31, 2026)]
    [InlineData(2027, 2, 28, 2026)]
    [InlineData(2027, 3, 1, 2027)]
    public void Tax_year_runs_March_to_February(int y, int m, int d, int startYear) =>
        Assert.Equal(new DateOnly(startYear, 3, 1), Ledger.TaxYearStart(new DateOnly(y, m, d)));

    [Fact]
    public void Top_payers_group_names_like_clients()
    {
        var top = Ledger.TopPayers(
            [In(Today, 100m, "Karoo Coffee Roasters"), In(Today, 50m, "karoo coffee  roasters"), In(Today, 120m, "Fynbos Physio"), In(Today, 5m)],
            Today.AddMonths(-1), Today);
        Assert.Equal([("Karoo Coffee Roasters", 150m), ("Fynbos Physio", 120m), ("Not said", 5m)], top.Select(t => (t.Name, t.Amount)));
    }

    [Fact]
    public void Expenses_by_category_put_blanks_under_other()
    {
        var totals = Ledger.ByCategory([Out(Today, 10m, "Domains"), Out(Today, 30m), Out(Today, 5m, "Domains")], Today, Today);
        Assert.Equal([("Other", 30m), ("Domains", 15m)], totals.Select(t => (t.Name, t.Amount)));
    }

    [Fact]
    public void Check_wants_a_real_amount_and_a_payee()
    {
        Assert.Empty(Ledger.Check(In(Today, 450m), Today));
        Assert.Contains("Type an amount more than R 0.", Ledger.Check(In(Today, 0m), Today));
        Assert.Contains("Amounts go to cents, two decimals at most.", Ledger.Check(In(Today, 1.005m), Today));
        Assert.Contains("Say who it was paid to.", Ledger.Check(Out(Today, 10m) with { Party = null }, Today));
    }

    [Fact]
    public void Monthly_expense_fills_in_each_month_up_to_today()
    {
        var due = RecurringExpenses.Due([Server(new DateOnly(2026, 8, 1))], new HashSet<string>(), Today, DateTimeOffset.UnixEpoch);
        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], due.Select(e => e.Date));
        Assert.All(due, e => Assert.Equal((EntryKind.Expense, 1899m, "xneelo server"), (e.Kind, e.Amount, e.Party)));
        Assert.Equal("recurring:r1:2026-10", due[^1].RecurringKey);
    }

    [Fact]
    public void Monthly_expense_waits_for_its_day_and_skips_what_exists()
    {
        var keys = new HashSet<string> { "recurring:r1:2026-09" };
        var due = RecurringExpenses.Due([Server(new DateOnly(2026, 9, 1), day: 31)], keys, new DateOnly(2026, 10, 30), DateTimeOffset.UnixEpoch);
        Assert.Empty(due);
        Assert.Equal(new DateOnly(2027, 2, 28), RecurringExpenses.DateIn(Server(Today, 31), new DateOnly(2027, 2, 1)));
    }

    [Fact]
    public void Paused_and_ended_monthly_expenses_stop()
    {
        Assert.Empty(RecurringExpenses.Due([Server(new DateOnly(2026, 10, 1)) with { Paused = true }], new HashSet<string>(), Today, DateTimeOffset.UnixEpoch));
        var ended = RecurringExpenses.Due([Server(new DateOnly(2026, 8, 1)) with { Ends = new DateOnly(2026, 8, 1) }], new HashSet<string>(), Today, DateTimeOffset.UnixEpoch);
        Assert.Single(ended);
        Assert.Contains("It can't end before it starts.", RecurringExpenses.Check(Server(Today) with { Ends = Today.AddMonths(-1) }));
    }

    [Fact]
    public void Resuming_does_not_fill_in_the_paused_months()
    {
        var paused = RecurringExpenses.SetPaused(Server(new DateOnly(2026, 6, 1)), true, new DateOnly(2026, 7, 2));
        var resumed = RecurringExpenses.SetPaused(paused, false, Today);
        var due = RecurringExpenses.Due([resumed], new HashSet<string>(), Today, DateTimeOffset.UnixEpoch);
        Assert.Equal([new DateOnly(2026, 10, 1)], due.Select(e => e.Date));
    }

    [Fact]
    public void Store_keeps_entries_encrypted_and_does_not_re_add_deleted_monthly_ones()
    {
        var path = Path.Combine(folder, "test.db");
        using (var store = new LocalStore(path, new Vault(RandomNumberGenerator.GetBytes(32))))
        {
            store.SaveEntry(In(Today, 2800m, "Fynbos Physio"));
            store.SaveRecurring(Server(new DateOnly(2026, 9, 1)));
            Assert.Equal(2, store.AddRecurringExpenses(Today));
            Assert.Equal(0, store.AddRecurringExpenses(Today));

            var october = store.ListEntries(new DateOnly(2026, 10, 1), Today);
            Assert.Equal(2, october.Count);
            store.DeleteEntry(october.Single(e => e.Kind == EntryKind.Expense).Id);
            Assert.Equal(0, store.AddRecurringExpenses(Today));
            Assert.Single(store.ListEntries(new DateOnly(2026, 10, 1), Today));

            store.DeleteRecurring("r1");
            Assert.Empty(store.ListRecurring());
            Assert.Single(store.ListEntries(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        }
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(path).Concat(File.Exists(path + "-wal") ? File.ReadAllBytes(path + "-wal") : []).ToArray();
        Assert.DoesNotContain("Fynbos", System.Text.Encoding.UTF8.GetString(bytes));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
