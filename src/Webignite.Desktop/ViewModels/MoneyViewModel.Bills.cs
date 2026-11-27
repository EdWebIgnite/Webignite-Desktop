using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Bills to pay: as read from Xero, or, when quotes and invoices are made in Webignite, kept here and marked paid
/// here (which adds the expense to the month).
/// </summary>
public partial class MoneyViewModel
{
    /// <summary>Bills are kept in Webignite, as quotes and invoices are (the switch in Settings, Quotes and invoices).</summary>
    public bool OwnBills => services.Settings.Documents.MakeInWebignite;
    public bool ShowXeroBills => !OwnBills;

    public ObservableCollection<OwnBillRowViewModel> OwnBillRows { get; } = [];
    public bool HasOwnBills => OwnBillRows.Count > 0;

    /// <summary>The first few shown; the rest are in Xero.</summary>
    public const int BillsShown = 8;

    public ObservableCollection<BillRowViewModel> BillRows { get; } = [];

    [ObservableProperty] public partial string BillsSummary { get; set; } = "";
    [ObservableProperty] public partial string BillsRead { get; set; } = "";
    [ObservableProperty] public partial bool ReadingBills { get; set; }

    public bool XeroConnected => services.XeroApi is not null;
    public bool HasBills => BillRows.Count > 0;

    private void RefreshBills()
    {
        OnPropertyChanged(nameof(OwnBills));
        OnPropertyChanged(nameof(ShowXeroBills));
        if (OwnBills)
        {
            RefreshOwnBills();
            return;
        }
        var today = Today;
        var state = services.XeroBills.State;
        BillRows.Clear();
        foreach (var bill in Bills.InOrder(state.Open).Take(BillsShown))
            BillRows.Add(new BillRowViewModel(bill, today));
        BillsSummary = state.Read is not null ? Bills.Summary(state.Open, today)
            : services.XeroApi is null ? "Connect Xero in Settings to see the bills you have to pay."
            : "Reading the bills from Xero…";
        BillsRead = state.Read is { } read
            ? $"From Xero at {read.ToLocalTime():HH:mm}." + (state.Open.Count > BillsShown ? $" {state.Open.Count - BillsShown} more in Xero." : "")
            : "";
        OnPropertyChanged(nameof(HasBills));
        OnPropertyChanged(nameof(XeroConnected));
    }

    private void RefreshOwnBills()
    {
        var today = Today;
        var bills = services.Store.ListSupplierBills();
        BillRows.Clear();
        OwnBillRows.Clear();
        // Every bill still owed, and those paid this month or last, so a payment just made stays in view.
        var since = Dates.MonthStart(today).AddMonths(-1);
        foreach (var bill in SupplierBills.InOrder(bills).Where(b => SupplierBills.IsOwed(b) || b.Paid >= since))
            OwnBillRows.Add(new OwnBillRowViewModel(bill, today, this));
        BillsSummary = SupplierBills.Summary(bills, today);
        BillsRead = "";
        OnPropertyChanged(nameof(HasOwnBills));
        OnPropertyChanged(nameof(HasBills));
    }

    /// <summary>Marks a bill paid today and adds its expense to the month, with the bill as its receipt.</summary>
    internal void PayBill(SupplierBill bill)
    {
        if (SupplierBills.Pay(bill, Today, DateTimeOffset.UtcNow) is not { } paid)
            return;
        services.Store.SavePaidBill(paid.Bill, paid.Expense);
        services.ShowNote?.Invoke($"Paid {bill.Supplier}", $"{Money.Rand(bill.Total)} is in this month's expenses.", null);
        Refresh();
    }

    internal void OpenBill(SupplierBill bill)
    {
        if (services.Receipts.PathOf(bill) is { } path)
            services.Go.ShowFile(path);
    }

    /// <summary>Removes a bill still owed; Undo puts it back.</summary>
    internal void DeleteBill(SupplierBill bill)
    {
        if (!SupplierBills.IsOwed(bill))
            return;
        services.Store.DeleteSupplierBill(bill.Id);
        RefreshOwnBills();
        services.OfferUndo($"Removed the bill from {bill.Supplier}", () =>
        {
            services.Store.SaveSupplierBill(bill);
            RefreshOwnBills();
        });
    }

    [RelayCommand]
    private async Task ReadBillsNow()
    {
        if (ReadingBills)
            return;
        ReadingBills = true;
        try
        {
            if (await services.Work.Run("Reading bills from Xero", () => services.XeroBills.RunAsync()) is { } problem)
                BillsRead = problem;
            else
                RefreshBills();
        }
        finally
        {
            ReadingBills = false;
        }
    }
}

/// <summary>A bill kept in Webignite on the Money page: still to pay (Paid marks it), or paid lately.</summary>
public sealed partial class OwnBillRowViewModel(SupplierBill bill, DateOnly today, MoneyViewModel owner) : ViewModelBase
{
    public string Supplier => bill.Supplier;
    public string Details => string.Join(" · ", new[]
    {
        bill.Number,
        bill.Description.Length > 0 ? bill.Description : null,
        bill.Paid is { } paid ? "Paid " + paid.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture)
            : bill.Due is { } due ? "Due " + due.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));
    public string Amount => Money.Rand(bill.Total);
    public bool IsOverdue => SupplierBills.IsOverdue(bill, today);
    public bool IsOwed => SupplierBills.IsOwed(bill);
    public bool HasFile => bill.Receipt is not null;

    [RelayCommand]
    private void Pay() => owner.PayBill(bill);

    [RelayCommand]
    private void Open() => owner.OpenBill(bill);

    [RelayCommand]
    private void Delete() => owner.DeleteBill(bill);
}

/// <summary>One bill on the Money page.</summary>
public sealed partial class BillRowViewModel(XeroBill bill, DateOnly today) : ViewModelBase
{
    public string Supplier => bill.Supplier;
    public string Details => string.Join(" · ", new[]
    {
        bill.Number ?? bill.Reference,
        Bills.AwaitingApproval(bill) ? "Draft, approve it in Xero" : bill.Due is { } due ? "Due " + due.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));
    public string Amount => Money.Rand(Bills.AwaitingApproval(bill) ? bill.Total : bill.AmountDue);
    public bool IsOverdue => Bills.IsOverdue(bill, today);

    [RelayCommand]
    private Task Open() => AppServices.OpenInBrowser(new Uri("https://go.xero.com/AccountsPayable/Edit.aspx?InvoiceID=" + Uri.EscapeDataString(bill.Id)));
}
