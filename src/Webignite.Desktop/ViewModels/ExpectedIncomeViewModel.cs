using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Money's Expected tab: money that may come in, typed by hand, month by month, with how sure each line is and
/// what the month is likely worth. Lines are ticked off as come in or lost. Kept on this PC only.
/// </summary>
public sealed partial class ExpectedIncomeViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices services;
    private string? editing;

    public ExpectedIncomeViewModel(AppServices services)
    {
        this.services = services;
        MonthChoices = [.. ExpectedRules.MonthChoices(Today).Select(m => new ExpectedMonthChoice(m))];
        Month = MonthChoices[0];
        Presets = [.. ExpectedRules.Presets.Select(p => new ExpectedPreset(p.Label, p.Percent, this))];
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public IReadOnlyList<ExpectedMonthChoice> MonthChoices { get; private set; }
    public IReadOnlyList<ExpectedPreset> Presets { get; }
    public IReadOnlyList<string> ClientNames { get; private set; } = [];

    public ObservableCollection<ExpectedMonthGroup> Months { get; } = [];
    public ObservableCollection<ExpectedRow> Settled { get; } = [];

    public bool HasMonths => Months.Count > 0;
    public bool NoMonths => Months.Count == 0;
    public bool HasSettled => Settled.Count > 0;

    [ObservableProperty] public partial string Possible { get; private set; } = "";
    [ObservableProperty] public partial string Likely { get; private set; } = "";
    [ObservableProperty] public partial string CameIn { get; private set; } = "";
    [ObservableProperty] public partial string CountLine { get; private set; } = "";

    // The line being typed

    [ObservableProperty] public partial string Who { get; set; } = "";
    [ObservableProperty] public partial string What { get; set; } = "";
    [ObservableProperty] public partial string Amount { get; set; } = "";
    [ObservableProperty] public partial ExpectedMonthChoice? Month { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentLabel))]
    public partial string Percent { get; set; } = "50";

    [ObservableProperty] public partial string Message { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddLabel))]
    public partial bool IsChanging { get; private set; }

    public string AddLabel => IsChanging ? "Save" : "Add";

    /// <summary>The word for the percentage typed, e.g. Likely.</summary>
    public string PercentLabel => ExpectedRules.ParsePercent(Percent) is { } percent ? ExpectedRules.Label(percent) : "";

    partial void OnPercentChanged(string value)
    {
        foreach (var preset in Presets)
            preset.IsOn = ExpectedRules.ParsePercent(value) == preset.Percent;
    }

    internal void Use(ExpectedPreset preset) => Percent = preset.Percent.ToString(CultureInfo.InvariantCulture);

    [RelayCommand]
    private void Add()
    {
        if (ExpectedRules.ParsePercent(Percent) is not { } percent)
        {
            Message = "How sure is a whole number from 0 to 100.";
            return;
        }
        var all = services.Settings.ExpectedIncome;
        var item = (editing is null ? null : all.FirstOrDefault(i => i.Id == editing)) ?? new ExpectedIncome { Id = ExpectedIncome.NewId() };
        item = item with
        {
            Who = Who.Trim(), What = What.Trim(), Amount = Money.Parse(Amount) ?? 0, Percent = percent,
            Month = Month?.Month ?? MonthChoices[0].Month,
        };
        if (ExpectedRules.Problem(item) is { } problem)
        {
            Message = problem;
            return;
        }
        services.Settings.ExpectedIncome = ExpectedRules.Put(all, item);
        ClearForm();
        Refresh();
    }

    [RelayCommand]
    private void CancelChange()
    {
        ClearForm();
    }

    private void ClearForm()
    {
        editing = null;
        IsChanging = false;
        Who = What = Amount = Message = "";
    }

    internal void Change(ExpectedIncome item)
    {
        editing = item.Id;
        IsChanging = true;
        Who = item.Who;
        What = item.What;
        Amount = item.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        Percent = item.Percent.ToString(CultureInfo.InvariantCulture);
        // A month already past is not among the choices: it is offered for this line.
        if (MonthChoices.All(m => m.Month != item.Month))
        {
            MonthChoices = [new ExpectedMonthChoice(item.Month), .. MonthChoices];
            OnPropertyChanged(nameof(MonthChoices));
        }
        Month = MonthChoices.First(m => m.Month == item.Month);
        Message = "";
    }

    internal void Settle(ExpectedIncome item, ExpectedOutcome outcome)
    {
        services.Settings.ExpectedIncome = ExpectedRules.Settle(services.Settings.ExpectedIncome, item.Id, outcome, Today);
        Refresh();
    }

    internal void Remove(ExpectedIncome item)
    {
        var before = services.Settings.ExpectedIncome;
        services.Settings.ExpectedIncome = ExpectedRules.Remove(before, item.Id);
        Refresh();
        services.OfferUndo($"Removed {ExpectedRow.NameOf(item)}", () =>
        {
            services.Settings.ExpectedIncome = ExpectedRules.Put(services.Settings.ExpectedIncome, item);
            Refresh();
        });
    }

    public void Refresh()
    {
        var all = services.Settings.ExpectedIncome;
        ClientNames = [.. services.Clients.List().Select(c => c.Called).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)];
        OnPropertyChanged(nameof(ClientNames));

        var months = ExpectedRules.Months(all);
        Months.Clear();
        foreach (var month in months)
            Months.Add(new ExpectedMonthGroup(month, this, Today));
        var settled = ExpectedRules.Settled(all);
        Settled.Clear();
        foreach (var item in settled.Take(30))
            Settled.Add(new ExpectedRow(item, this));

        var open = months.SelectMany(m => m.Items).ToList();
        Possible = Money.Rand(open.Sum(i => i.Amount));
        Likely = Money.Rand(open.Sum(ExpectedRules.Likely));
        CameIn = Money.Rand(settled.Where(i => i.Outcome == ExpectedOutcome.CameIn && i.Settled?.Year == Today.Year).Sum(i => i.Amount));
        CountLine = open.Count switch
        {
            0 => "Nothing on the list yet.",
            1 => "From 1 line.",
            var n => $"From {n} lines, over {months.Count} {(months.Count == 1 ? "month" : "months")}.",
        };
        OnPropertyChanged(nameof(HasMonths));
        OnPropertyChanged(nameof(NoMonths));
        OnPropertyChanged(nameof(HasSettled));
    }
}

/// <summary>A month to pick when adding a line, e.g. "January 2027". The name follows the PC's region.</summary>
public sealed record ExpectedMonthChoice(DateOnly Month)
{
    public string Name => Month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    public override string ToString() => Name;
}

/// <summary>Confirmed, Likely, Maybe or Long shot: a click fills in its percentage.</summary>
public sealed partial class ExpectedPreset(string label, int percent, ExpectedIncomeViewModel owner) : ViewModelBase
{
    public string Label { get; } = label;
    public int Percent { get; } = percent;
    public string Tip => $"{Percent}% sure";

    [ObservableProperty] public partial bool IsOn { get; set; } = percent == 50;

    [RelayCommand]
    private void Pick() => owner.Use(this);
}

/// <summary>A month on the list, with its lines and what it adds up to.</summary>
public sealed class ExpectedMonthGroup(ExpectedMonth month, ExpectedIncomeViewModel owner, DateOnly today)
{
    public string Name => month.Month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    public bool IsPast => month.Month < new DateOnly(today.Year, today.Month, 1);
    public string Totals => $"{Money.Rand(month.Likely)} likely  ·  {Money.Rand(month.Possible)} if all of it comes in";
    public IReadOnlyList<ExpectedRow> Rows { get; } = [.. month.Items.Select(i => new ExpectedRow(i, owner))];
}

/// <summary>One line of expected income.</summary>
public sealed partial class ExpectedRow(ExpectedIncome item, ExpectedIncomeViewModel owner)
{
    public static string NameOf(ExpectedIncome item) =>
        string.Join(": ", new[] { item.Who, item.What }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Name => item.Who.Length > 0 ? item.Who : item.What;
    public string Detail => item.Who.Length > 0 ? item.What : "";
    public bool HasDetail => Detail.Length > 0;
    public string Amount => Money.Rand(item.Amount);
    public string Sure => $"{ExpectedRules.Label(item.Percent)}, {item.Percent.ToString(CultureInfo.InvariantCulture)}%";
    public string LikelyText => Money.Rand(ExpectedRules.Likely(item)) + " likely";
    public bool IsOpen => item.Outcome == ExpectedOutcome.Open;
    public bool CameIn => item.Outcome == ExpectedOutcome.CameIn;
    public bool Lost => item.Outcome == ExpectedOutcome.Lost;
    public string Outcome => (CameIn ? "Came in" : "Lost") + (item.Settled is { } day ? ", " + day.ToString("d MMM", CultureInfo.CurrentCulture) : "");
    public string MonthName => item.Month.ToString("MMM yyyy", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void MarkCameIn() => owner.Settle(item, ExpectedOutcome.CameIn);

    [RelayCommand]
    private void MarkLost() => owner.Settle(item, ExpectedOutcome.Lost);

    [RelayCommand]
    private void PutBack() => owner.Settle(item, ExpectedOutcome.Open);

    [RelayCommand]
    private void Change() => owner.Change(item);

    [RelayCommand]
    private void Remove() => owner.Remove(item);
}
