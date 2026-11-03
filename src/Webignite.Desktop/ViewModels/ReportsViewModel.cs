using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Desktop.ViewModels;

/// <summary>Reports from the money typed into the Money module: month by month, tax year, clients and costs.</summary>
public partial class ReportsViewModel : ViewModelBase, IRefreshable
{
    private const double BarWidth = 220;
    private readonly AppServices services;

    public ReportsViewModel(AppServices services)
    {
        this.services = services;
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public ObservableCollection<MonthRowViewModel> Months { get; } = [];
    public ObservableCollection<TotalRowViewModel> Payers { get; } = [];
    public ObservableCollection<TotalRowViewModel> Categories { get; } = [];

    [ObservableProperty] public partial string TaxYearTitle { get; set; } = "";
    [ObservableProperty] public partial string TaxYearReceived { get; set; } = "";
    [ObservableProperty] public partial string TaxYearExpenses { get; set; } = "";
    [ObservableProperty] public partial string TaxYearLeftOver { get; set; } = "";
    [ObservableProperty] public partial bool TaxYearShort { get; set; }
    [ObservableProperty] public partial string AverageReceived { get; set; } = "";

    /// <summary>"R 12 000.00 more than by this date last tax year." Empty when there is nothing to compare.</summary>
    [ObservableProperty] public partial string TaxYearChange { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public bool HasPayers => Payers.Count > 0;
    public bool HasCategories => Categories.Count > 0;

    public void Refresh()
    {
        services.Store.AddRecurringExpenses(Today);
        var from = Ledger.MonthOf(Today).AddMonths(-11);
        var taxStart = Ledger.TaxYearStart(Today);
        // Back to the start of last tax year, so this tax year can be set against it.
        var start = taxStart.AddYears(-1) < from ? taxStart.AddYears(-1) : from;
        var entries = services.Store.ListEntries(start, Today.AddYears(1));
        var lastYear = entries.Where(e => e.Date >= from).ToList();
        IsEmpty = !entries.Any(e => e.Date >= from || e.Date >= taxStart);

        var months = Ledger.Months(lastYear, Today, 12);
        var max = Math.Max(1m, months.Max(m => Math.Max(m.Received, m.Expenses)));
        Months.Clear();
        foreach (var m in months.Reverse())
            Months.Add(new MonthRowViewModel(m, (double)(m.Received / max) * BarWidth, (double)(m.Expenses / max) * BarWidth));

        var taxYear = entries.Where(e => e.Date >= taxStart).ToList();
        var received = Ledger.Sum(taxYear, EntryKind.Received);
        var expenses = Ledger.Sum(taxYear, EntryKind.Expense);
        TaxYearTitle = $"Tax year so far, 1 March {taxStart.Year} to today";
        TaxYearReceived = Money.Rand(received);
        TaxYearExpenses = Money.Rand(expenses);
        TaxYearLeftOver = Money.Rand(received - expenses);
        TaxYearShort = received < expenses;
        TaxYearChange = Trends.AgainstLastTaxYear(entries, Today) ?? "";

        var fullMonths = months.Take(11).Where(m => m.Received > 0 || m.Expenses > 0).ToList();
        AverageReceived = fullMonths.Count == 0 ? "" : $"On average {Money.Rand(fullMonths.Average(m => m.Received))} a month came in over the last {fullMonths.Count} full months.";

        Payers.Clear();
        var payers = Ledger.TopPayers(lastYear, from, Today, 8);
        foreach (var p in payers)
            Payers.Add(new TotalRowViewModel(p, (double)(p.Amount / Math.Max(1m, payers[0].Amount))));
        Categories.Clear();
        var categories = Ledger.ByCategory(lastYear, from, Today);
        foreach (var c in categories)
            Categories.Add(new TotalRowViewModel(c, (double)(c.Amount / Math.Max(1m, categories[0].Amount))));
        OnPropertyChanged(nameof(HasPayers));
        OnPropertyChanged(nameof(HasCategories));
    }
}

public sealed class MonthRowViewModel(MonthTotals totals, double receivedBar, double expensesBar)
{
    public string Month => totals.First.ToString("MMM yyyy");
    public string Received => Money.Rand(totals.Received);
    public string Expenses => Money.Rand(totals.Expenses);
    public string LeftOver => Money.Rand(totals.LeftOver);
    public bool IsShort => totals.LeftOver < 0;
    public double ReceivedBar => receivedBar;
    public double ExpensesBar => expensesBar;
}

/// <summary>A row in the clients or costs list. Share is this amount against the biggest in the list, 0 to 1.</summary>
public sealed class TotalRowViewModel(NamedTotal total, double share)
{
    public string Name => total.Name;
    public string Amount => Money.Rand(total.Amount);
    public double Share => share;
}
