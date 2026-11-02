using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Desktop.ViewModels;

/// <summary>The income projection on the Money page: expected against came in, 5 months back to 6 ahead.</summary>
public partial class MoneyViewModel
{
    private IncomePlan plan = new();

    /// <summary>This month's row is in bold.</summary>
    public static FuncValueConverter<bool, FontWeight> BoldIf { get; } = new(on => on ? FontWeight.SemiBold : FontWeight.Normal);

    [ObservableProperty] public partial IReadOnlyList<ProjectionMonth> ProjectionMonths { get; set; } = [];
    [ObservableProperty] public partial string IncomeSummary { get; set; } = "";
    [ObservableProperty] public partial string ExpectedTitle { get; set; } = "";
    [ObservableProperty] public partial string ExpectedNote { get; set; } = "";

    public ObservableCollection<IncomeMonthRow> IncomeRows { get; } = [];
    public ObservableCollection<ExpectedItemRow> ExpectedItems { get; } = [];
    public bool HasExpectedItems => ExpectedItems.Count > 0;

    public ChartStyle ChartStyle => plan.Chart;
    public bool IsAuto => plan.Expected == ExpectedSource.Auto;
    public bool IsManual => !IsAuto;
    public bool FromXero => plan.CameIn == CameInSource.Xero;
    public bool FromMoney => !FromXero;
    public bool ChartBars => plan.Chart == ChartStyle.Bars;
    public bool ChartFilled => plan.Chart == ChartStyle.Filled;
    public bool ChartLines => plan.Chart == ChartStyle.Lines;

    [RelayCommand]
    private void UseAuto() => ChangePlan(plan with { Expected = ExpectedSource.Auto });

    [RelayCommand]
    private void UseManual() => ChangePlan(plan with { Expected = ExpectedSource.Manual });

    [RelayCommand]
    private void CameInFromMoney() => ChangePlan(plan with { CameIn = CameInSource.Money });

    [RelayCommand]
    private void CameInFromXero() => ChangePlan(plan with { CameIn = CameInSource.Xero });

    [RelayCommand]
    private void ShowChart(ChartStyle style) => ChangePlan(plan with { Chart = style }, rows: false);

    private void ChangePlan(IncomePlan changed, bool rows = true)
    {
        plan = changed;
        services.IncomePlan = changed;
        UpdateIncome(rows);
    }

    /// <summary>A typed amount for one month. Saved as it is typed, without rebuilding the rows.</summary>
    internal void SetManual(DateOnly month, decimal? amount)
    {
        plan = Projection.SetManual(plan, month, amount);
        services.IncomePlan = plan;
        UpdateIncome(rows: false);
    }

    private void LoadIncome()
    {
        plan = services.IncomePlan;
        UpdateIncome(rows: true);
    }

    private void UpdateIncome(bool rows)
    {
        var first = Projection.FirstMonth(Today);
        var documents = services.Store.List(DocumentKind.Invoice);
        var entries = services.Store.ListEntries(first, first.AddMonths(Projection.MonthCount).AddDays(-1));
        var retainers = services.Store.ListRetainers();
        var renewals = services.Store.ListRenewals();
        var drafted = services.Store.DraftsMade().Keys.ToHashSet();

        var months = Projection.Months(plan, entries, documents, retainers, renewals, drafted, Today);
        ProjectionMonths = months;

        if (rows)
        {
            IncomeRows.Clear();
            foreach (var m in months)
                IncomeRows.Add(new IncomeMonthRow(m, this, plan.Manual.TryGetValue(Projection.Key(m.Month), out var typed) ? typed : null));
        }
        else
        {
            foreach (var row in IncomeRows)
                row.Update(months.First(m => m.Month == row.Month));
        }

        var current = months.First(m => m.IsCurrent);
        var name = current.Month.ToString("MMMM", CultureInfo.InvariantCulture);
        IncomeSummary = current.Expected is { } expected
            ? $"{name}: {Money.Rand(current.CameIn)} of {Money.Rand(expected)} in so far."
            : $"{name}: {Money.Rand(current.CameIn)} in so far. Nothing typed in as expected yet.";

        // What makes up the auto figure for the month shown at the top of the page.
        ExpectedItems.Clear();
        ExpectedTitle = $"Expected in {Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        if (IsAuto)
        {
            foreach (var item in Projection.ExpectedIn(Month, documents, retainers, renewals, drafted))
                ExpectedItems.Add(new ExpectedItemRow(item));
            ExpectedNote = ExpectedItems.Count == 0
                ? "No invoices due, retainers or charged renewals this month."
                : "Invoices due this month, plus retainers and renewals still to be invoiced.";
        }
        else
        {
            ExpectedNote = "Switch to Work it out to see invoices, retainers and renewals here.";
        }

        OnPropertyChanged(nameof(HasExpectedItems));
        OnPropertyChanged(nameof(ChartStyle));
        OnPropertyChanged(nameof(IsAuto));
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(FromXero));
        OnPropertyChanged(nameof(FromMoney));
        OnPropertyChanged(nameof(ChartBars));
        OnPropertyChanged(nameof(ChartFilled));
        OnPropertyChanged(nameof(ChartLines));
    }
}

public partial class IncomeMonthRow : ViewModelBase
{
    private readonly MoneyViewModel owner;
    private bool loading;

    public IncomeMonthRow(ProjectionMonth month, MoneyViewModel owner, decimal? typed)
    {
        this.owner = owner;
        Month = month.Month;
        loading = true;
        ManualText = typed is { } t ? t.ToString("0.##", CultureInfo.InvariantCulture) : "";
        loading = false;
        Update(month);
    }

    public DateOnly Month { get; }
    public string Title => Month.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public bool IsManual => owner.IsManual;
    public bool IsAuto => owner.IsAuto;

    [ObservableProperty] public partial string Expected { get; set; } = "";
    [ObservableProperty] public partial string CameIn { get; set; } = "";
    [ObservableProperty] public partial string Gap { get; set; } = "";
    [ObservableProperty] public partial bool IsCurrent { get; set; }
    [ObservableProperty] public partial bool IsFuture { get; set; }
    [ObservableProperty] public partial bool IsShort { get; set; }
    [ObservableProperty] public partial string ManualText { get; set; }

    partial void OnManualTextChanged(string value)
    {
        if (loading)
            return;
        var amount = string.IsNullOrWhiteSpace(value) ? null : Money.Parse(value);
        if (amount is null && !string.IsNullOrWhiteSpace(value))
            return;
        owner.SetManual(Month, amount);
    }

    internal void Update(ProjectionMonth month)
    {
        IsCurrent = month.IsCurrent;
        IsFuture = month.IsFuture;
        Expected = month.Expected is { } e ? Money.Rand(e) : "Not set";
        CameIn = month.IsFuture ? "" : Money.Rand(month.CameIn);
        IsShort = month.IsPast && month.Expected is > 0 && month.Gap > 0;
        Gap = month.Expected is not > 0 ? ""
            : month.IsFuture ? ""
            : month.Gap > 0 ? (month.IsPast ? $"{Money.Rand(month.Gap)} short" : $"{Money.Rand(month.Gap)} to come")
            : month.Gap < 0 ? $"{Money.Rand(-month.Gap)} more"
            : "On target";
    }
}

public sealed class ExpectedItemRow(ExpectedItem item)
{
    public string Date => item.Date.ToString("d MMM", CultureInfo.InvariantCulture);
    public string What => item.What;
    public string Amount => Money.Rand(item.Amount);
}
