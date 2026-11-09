using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Xero;
using Webignite.Xero;

namespace Webignite.Desktop.ViewModels;

/// <summary>Drafting a supplier's bill in Xero from their PDF: read, checked here, then sent as a draft.</summary>
public partial class MoneyViewModel
{
    private byte[]? billPdf;
    private string? billPdfName;

    public ObservableCollection<XeroSalesAccount> ExpenseAccounts { get; } = [];

    [ObservableProperty] public partial bool DraftingBill { get; set; }
    [ObservableProperty] public partial bool BillBusy { get; set; }
    [ObservableProperty] public partial string BillSupplier { get; set; } = "";
    [ObservableProperty] public partial string BillNumber { get; set; } = "";
    [ObservableProperty] public partial DateTime? BillDate { get; set; }
    [ObservableProperty] public partial DateTime? BillDue { get; set; }
    [ObservableProperty] public partial string BillTotal { get; set; } = "";
    [ObservableProperty] public partial string BillDescription { get; set; } = "";
    [ObservableProperty] public partial XeroSalesAccount? BillAccount { get; set; }
    [ObservableProperty] public partial string BillMessage { get; set; } = "";
    [ObservableProperty] public partial bool BillMessageIsProblem { get; set; }
    [ObservableProperty] public partial bool AskAddSupplier { get; set; }

    public string BillFileName => billPdfName ?? "";

    /// <summary>Reads the supplier's PDF into the boxes. Called by the page once a PDF was picked.</summary>
    public async Task StartBillAsync(string path)
    {
        BillMessage = "";
        AskAddSupplier = false;
        billPdfName = Path.GetFileName(path);
        OnPropertyChanged(nameof(BillFileName));
        DraftingBill = true;
        BillBusy = true;
        try
        {
            billPdf = await File.ReadAllBytesAsync(path);
            var text = "";
            try
            {
                var bytes = billPdf;
                text = await Task.Run(() => Webignite.Tools.PdfText.Read(bytes, maxPages: 3));
            }
            catch (InvalidDataException)
            {
                // Not readable as a PDF: the boxes stay empty to fill in.
            }
            IReadOnlyList<string> suppliers = [];
            if (services.XeroApi is { } api)
            {
                suppliers = [.. (await api.ContactsAsync()).Where(c => c.Active).Select(c => c.Name)];
                if (ExpenseAccounts.Count == 0)
                    foreach (var account in await api.ExpenseAccountsAsync())
                        ExpenseAccounts.Add(account);
                BillAccount = ExpenseAccounts.FirstOrDefault(a => a.Code == services.Settings.Xero.BillAccountCode);
            }
            var read = BillReader.Read(text, suppliers);
            BillSupplier = read.Supplier;
            BillNumber = read.Number ?? "";
            BillDate = read.Date?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
            BillDue = read.Due?.ToDateTime(TimeOnly.MinValue);
            BillTotal = read.Total?.ToString("0.00", CultureInfo.CurrentCulture) ?? "";
            BillDescription = read.Description;
            if (text.Trim().Length == 0)
                SayBill("Couldn't read any words in this PDF (it may be a scan). Fill in the details.", problem: true);
            else
                SayBill("Read from the PDF. Check each box before it goes to Xero.", problem: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XeroException or HttpRequestException or TaskCanceledException)
        {
            SayBill("Couldn't get it ready: " + e.Message, problem: true);
        }
        finally
        {
            BillBusy = false;
        }
    }

    [RelayCommand]
    private void CancelBill()
    {
        DraftingBill = false;
        billPdf = null;
        billPdfName = null;
    }

    [RelayCommand]
    private Task DraftBill() => SendBill(addSupplier: false);

    [RelayCommand]
    private Task AddSupplierAndDraft() => SendBill(addSupplier: true);

    private async Task SendBill(bool addSupplier)
    {
        var bill = new BillDraft
        {
            Supplier = BillSupplier.Trim(),
            Number = string.IsNullOrWhiteSpace(BillNumber) ? null : BillNumber.Trim(),
            Date = BillDate is { } d ? DateOnly.FromDateTime(d) : null,
            Due = BillDue is { } due ? DateOnly.FromDateTime(due) : null,
            Total = Money.Parse(BillTotal),
            Description = BillDescription.Trim(),
        };
        if (BillReader.Check(bill) is { } problem)
        {
            SayBill(problem, problem: true);
            return;
        }
        if (BillAccount is not { } account)
        {
            SayBill("Pick the expense account it goes to.", problem: true);
            return;
        }
        if (services.XeroApi is not { } api)
        {
            SayBill("Connect Xero in Settings first.", problem: true);
            return;
        }
        services.Settings.Xero = services.Settings.Xero with { BillAccountCode = account.Code };
        BillBusy = true;
        AskAddSupplier = false;
        SayBill("Talking to Xero…", problem: false);
        try
        {
            var result = await services.Work.Run("Drafting a bill in Xero", () => new XeroBillDrafts(api).DraftAsync(bill, account.Code, billPdf, billPdfName, addSupplier));
            switch (result)
            {
                case XeroBillDrafts.Result.NeedsSupplier needs:
                    AskAddSupplier = needs.Question.Contains("Add them", StringComparison.Ordinal);
                    SayBill(needs.Question, problem: true);
                    break;
                case XeroBillDrafts.Result.Done done:
                    DraftingBill = false;
                    billPdf = null;
                    await services.XeroBills.RunAsync();
                    RefreshBills();
                    BillsRead = done.Problem ?? $"Bill from {bill.Supplier} for {Money.Rand(bill.Total!.Value)} is a draft in Xero. Approve it there.";
                    break;
            }
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            SayBill("Couldn't draft it in Xero: " + e.Message, problem: true);
        }
        finally
        {
            BillBusy = false;
        }
    }

    private void SayBill(string text, bool problem)
    {
        BillMessage = text;
        BillMessageIsProblem = problem;
    }
}
