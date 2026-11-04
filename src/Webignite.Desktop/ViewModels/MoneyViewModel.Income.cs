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

    public ChartStyle ChartStyle => plan.ChartShown;
    public bool IsAuto => plan.Expected == ExpectedSource.Auto;
    public bool IsManual => !IsAuto;
    public bool FromXero => plan.CameIn == CameInSource.Xero;
    public bool FromMoney => !FromXero;
    public bool ChartBars => plan.ChartShown == ChartStyle.Bars;
    public bool ChartFilled => plan.ChartShown == ChartStyle.Filled;
    public bool ChartLines => plan.ChartShown == ChartStyle.Lines;

    [RelayCommand]
    private void UseAuto() => ChangePlan(plan with { Expected = ExpectedSource.Auto });

    [RelayCommand]
    private void UseManual() => ChangePlan(plan with { Expected = ExpectedSource.Manual });

    [RelayCommand]
    private void CameInFromMoney() => ChangePlan(plan with { CameIn = CameInSource.Money });

    [RelayCommand]
    private void CameInFromXero() => ChangePlan(plan with { CameIn = CameInSource.Xero });

    [RelayCommand]
    private void ShowChart(ChartStyle style) => ChangePlan(plan with { Chart = style, ChartPicked = true }, rows: false);

    private void ChangePlan(IncomePlan changed, bool rows = true)
    {
        planSave?.Stop(); // saved together with this change
        plan = changed;
        services.Settings.IncomePlan = changed;
        UpdateIncome(rows);
    }

    private Avalonia.Threading.DispatcherTimer? planSave;

    /// <summary>
    /// A typed amount for one month. The figures update as it is typed; it is saved once typing pauses, or when
    /// the app closes, not on every key.
    /// </summary>
    internal void SetManual(DateOnly month, decimal? amount)
    {
        plan = Projection.SetManual(plan, month, amount);
        UpdateIncome(rows: false);
        if (planSave is null)
        {
            planSave = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            planSave.Tick += (_, _) => SaveTyped();
        }
        planSave.Stop();
        planSave.Start();
    }

    /// <summary>Saves typed amounts still waiting for a pause in typing.</summary>
    internal void SaveTyped()
    {
        if (planSave is not { IsEnabled: true })
            return;
        planSave.Stop();
        services.Settings.IncomePlan = plan;
    }

    private void LoadIncome()
    {
        SaveTyped();
        plan = services.Settings.IncomePlan;
        UpdateIncome(rows: true);
    }

    // What the projection is worked out from, read when the rows are built. Typing an amount or switching the
    // chart reuses it instead of reading invoices, entries, retainers and renewals again on every key.
    private (IReadOnlyList<StoredDocument> Documents, IReadOnlyList<MoneyEntry> Entries, IReadOnlyList<Webignite.Core.Retainers.Retainer> Retainers,
        IReadOnlyList<Webignite.Core.Renewals.Renewal> Renewals, HashSet<string> Drafted)? incomeData;

    private void UpdateIncome(bool rows)
    {
        if (rows || incomeData is null)
        {
            var first = Projection.FirstMonth(Today);
            incomeData = (services.Store.List(DocumentKind.Invoice),
                services.Store.ListEntries(first, first.AddMonths(Projection.MonthCount).AddDays(-1)),
                services.Store.ListRetainers(), services.Store.ListRenewals(), services.Store.DraftsMade().Keys.ToHashSet());
        }
        var (documents, entries, retainers, renewals, drafted) = incomeData.Value;

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
