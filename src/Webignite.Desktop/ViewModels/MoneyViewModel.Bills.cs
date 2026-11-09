using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Desktop.ViewModels;

/// <summary>Bills to pay, as read from Xero. Paying them is done in the bank and Xero.</summary>
public partial class MoneyViewModel
{
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
