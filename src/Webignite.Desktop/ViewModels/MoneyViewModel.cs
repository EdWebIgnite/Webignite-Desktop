using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Money module: what came in and went out in a month, and the expenses that repeat monthly.
/// Amounts are typed in here by hand. Nothing is sent to Xero.
/// </summary>
public partial class MoneyViewModel : ViewModelBase
{
    private readonly AppServices services;

    public MoneyViewModel(AppServices services)
    {
        this.services = services;
        Month = Ledger.MonthOf(Today);
        OutCategory = ExpenseCategories.All[0];
        RecCategory = ExpenseCategories.All[0];
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public IReadOnlyList<string> Categories => ExpenseCategories.All;
    public ObservableCollection<string> ClientNames { get; } = [];
    public ObservableCollection<EntryRowViewModel> ReceivedRows { get; } = [];
    public ObservableCollection<EntryRowViewModel> ExpenseRows { get; } = [];
    public ObservableCollection<RecurringRowViewModel> RecurringRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthTitle), nameof(IsThisMonth))]
    public partial DateOnly Month { get; set; }

    public string MonthTitle => Month.ToString("MMMM yyyy");
    public bool IsThisMonth => Month == Ledger.MonthOf(Today);

    [ObservableProperty] public partial string Received { get; set; } = "";
    [ObservableProperty] public partial string Expenses { get; set; } = "";
    [ObservableProperty] public partial string LeftOver { get; set; } = "";
    [ObservableProperty] public partial bool IsShort { get; set; }
    [ObservableProperty] public partial string RecurringSummary { get; set; } = "";

    // Money received
    [ObservableProperty] public partial DateTime? InDate { get; set; }
    [ObservableProperty] public partial string InAmount { get; set; } = "";
    [ObservableProperty] public partial string InFrom { get; set; } = "";
    [ObservableProperty] public partial string InNote { get; set; } = "";
    [ObservableProperty] public partial string InMessage { get; set; } = "";

    // Expense
    [ObservableProperty] public partial DateTime? OutDate { get; set; }
    [ObservableProperty] public partial string OutAmount { get; set; } = "";
    [ObservableProperty] public partial string OutTo { get; set; } = "";
    [ObservableProperty] public partial string OutCategory { get; set; }
    [ObservableProperty] public partial string OutMessage { get; set; } = "";

    // Monthly expense
    [ObservableProperty] public partial string RecName { get; set; } = "";
    [ObservableProperty] public partial string RecAmount { get; set; } = "";
    [ObservableProperty] public partial string RecDay { get; set; } = "1";
    [ObservableProperty] public partial string RecCategory { get; set; }
    [ObservableProperty] public partial string RecMessage { get; set; } = "";

    public bool HasReceived => ReceivedRows.Count > 0;
    public bool HasExpenses => ExpenseRows.Count > 0;
    public bool HasRecurring => RecurringRows.Count > 0;

    [RelayCommand]
    private void PreviousMonth() => ShowMonth(Month.AddMonths(-1));

    [RelayCommand]
    private void NextMonth() => ShowMonth(Month.AddMonths(1));

    [RelayCommand]
    private void ThisMonth() => ShowMonth(Ledger.MonthOf(Today));

    private void ShowMonth(DateOnly month)
    {
        Month = month;
        Refresh();
    }

    [RelayCommand]
    private void AddReceived()
    {
        var entry = new MoneyEntry
        {
            Id = MoneyEntry.NewId(),
            Kind = EntryKind.Received,
            Date = InDate is { } d ? DateOnly.FromDateTime(d) : Today,
            Amount = Money.Parse(InAmount) ?? 0,
            Party = Blank(InFrom),
            Note = Blank(InNote),
            Created = DateTimeOffset.UtcNow,
        };
        if (Problem(entry) is { } problem)
        {
            InMessage = problem;
            return;
        }
        services.Store.SaveEntry(entry);
        InAmount = InFrom = InNote = InMessage = "";
        ShowMonth(Ledger.MonthOf(entry.Date));
    }

    [RelayCommand]
    private void AddExpense()
    {
        var entry = new MoneyEntry
        {
            Id = MoneyEntry.NewId(),
            Kind = EntryKind.Expense,
            Date = OutDate is { } d ? DateOnly.FromDateTime(d) : Today,
            Amount = Money.Parse(OutAmount) ?? 0,
            Party = Blank(OutTo),
            Category = OutCategory,
            Created = DateTimeOffset.UtcNow,
        };
        if (Problem(entry) is { } problem)
        {
            OutMessage = problem;
            return;
        }
        services.Store.SaveEntry(entry);
        OutAmount = OutTo = OutMessage = "";
        ShowMonth(Ledger.MonthOf(entry.Date));
    }

    private static string? Problem(MoneyEntry entry) => Ledger.Check(entry, Today).FirstOrDefault();

    [RelayCommand]
    private void AddRecurring()
    {
        var recurring = new RecurringExpense
        {
            Id = RecurringExpense.NewId(),
            Name = RecName.Trim(),
            Amount = Money.Parse(RecAmount) ?? 0,
            DayOfMonth = int.TryParse(RecDay.Trim(), out var day) ? day : 0,
            Category = RecCategory,
            Starts = Ledger.MonthOf(Today),
            Created = DateTimeOffset.UtcNow,
        };
        if (RecurringExpenses.Check(recurring).FirstOrDefault() is { } problem)
        {
            RecMessage = problem;
            return;
        }
        services.Store.SaveRecurring(recurring);
        RecName = RecAmount = RecMessage = "";
        RecDay = "1";
        Refresh();
    }

    public void Delete(MoneyEntry entry)
    {
        services.Store.DeleteEntry(entry.Id);
        Refresh();
    }

    public void SetPaused(RecurringExpense recurring, bool paused)
    {
        services.Store.SaveRecurring(RecurringExpenses.SetPaused(recurring, paused, Today));
        Refresh();
    }

    public void Stop(RecurringExpense recurring)
    {
        services.Store.DeleteRecurring(recurring.Id);
        Refresh();
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Adds any monthly expenses that have come round, then shows the month.</summary>
    public void Refresh()
    {
        services.Store.AddRecurringExpenses(Today);
        var entries = services.Store.ListEntries(Month, Month.AddMonths(1).AddDays(-1));

        ReceivedRows.Clear();
        foreach (var e in entries.Where(e => e.Kind == EntryKind.Received).OrderByDescending(e => e.Date))
            ReceivedRows.Add(new EntryRowViewModel(e, this));
        ExpenseRows.Clear();
        foreach (var e in entries.Where(e => e.Kind == EntryKind.Expense).OrderByDescending(e => e.Date))
            ExpenseRows.Add(new EntryRowViewModel(e, this));

        var totals = Ledger.Months(entries, Month, 1)[0];
        Received = Money.Rand(totals.Received);
        Expenses = Money.Rand(totals.Expenses);
        LeftOver = Money.Rand(totals.LeftOver);
        IsShort = totals.LeftOver < 0;

        var recurring = services.Store.ListRecurring();
        RecurringRows.Clear();
        foreach (var r in recurring)
            RecurringRows.Add(new RecurringRowViewModel(r, this));
        var monthly = RecurringExpenses.MonthlyTotal(recurring, Today);
        RecurringSummary = recurring.Count == 0
            ? "Set up anything you pay every month once, and it is added to your expenses on its day."
            : $"{Money.Rand(monthly)} a month. Each one is added to your expenses on its day.";

        ClientNames.Clear();
        foreach (var client in services.Store.ListClients())
            ClientNames.Add(client.Name);

        var defaultDate = (IsThisMonth ? Today : Month).ToDateTime(TimeOnly.MinValue);
        InDate = defaultDate;
        OutDate = defaultDate;

        OnPropertyChanged(nameof(HasReceived));
        OnPropertyChanged(nameof(HasExpenses));
        OnPropertyChanged(nameof(HasRecurring));
    }

    internal static string Ordinal(int day) => day switch
    {
        31 => "last day",
        1 or 21 => $"{day}st",
        2 or 22 => $"{day}nd",
        3 or 23 => $"{day}rd",
        _ => $"{day}th",
    };
}

public partial class EntryRowViewModel(MoneyEntry entry, MoneyViewModel owner) : ViewModelBase
{
    public string Date => entry.Date.ToString("d MMM");
    public string Party => entry.Party ?? (entry.Kind == EntryKind.Received ? "Not said" : "");
    public string Detail => string.Join("  ·  ", new[] { entry.Category, entry.Note, entry.RecurringKey is null ? null : "Monthly" }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool HasDetail => Detail.Length > 0;
    public string Amount => Money.Rand(entry.Amount);

    [RelayCommand]
    private void Delete() => owner.Delete(entry);
}

public partial class RecurringRowViewModel(RecurringExpense recurring, MoneyViewModel owner) : ViewModelBase
{
    public string Name => recurring.Name;
    public string Detail => recurring.Paused
        ? "Paused"
        : $"On the {MoneyViewModel.Ordinal(recurring.DayOfMonth)} of the month" + (recurring.Category is { } c ? $"  ·  {c}" : "");
    public string Amount => Money.Rand(recurring.Amount);
    public string PauseLabel => recurring.Paused ? "Resume" : "Pause";
    public bool IsPaused => recurring.Paused;

    [ObservableProperty] public partial bool ConfirmingStop { get; set; }

    [RelayCommand]
    private void TogglePause() => owner.SetPaused(recurring, !recurring.Paused);

    [RelayCommand]
    private void AskStop() => ConfirmingStop = true;

    [RelayCommand]
    private void CancelStop() => ConfirmingStop = false;

    [RelayCommand]
    private void Stop() => owner.Stop(recurring);
}
