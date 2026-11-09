using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Xero;

namespace Webignite.Desktop.ViewModels;

/// <summary>Recording a payment against the invoice in Xero, only when clicked here.</summary>
public partial class DocumentEditorViewModel
{
    public ObservableCollection<XeroBankAccount> BankAccounts { get; } = [];

    [ObservableProperty]
    public partial bool RecordingPayment { get; set; }

    [ObservableProperty]
    public partial string PaymentAmountText { get; set; } = "";

    [ObservableProperty]
    public partial DateTime? PaymentDay { get; set; }

    [ObservableProperty]
    public partial XeroBankAccount? PaymentAccount { get; set; }

    public bool CanRecordPayment => stored is not null && InvoicePayments.CanRecord(stored) && !XeroBusy && !RecordingPayment;

    public string PaymentHint => stored is null ? "" : $"{Money.Rand(InvoicePayments.Due(stored))} is still owed. It's recorded in Xero against this invoice.";

    partial void OnRecordingPaymentChanged(bool value) => OnPropertyChanged(nameof(CanRecordPayment));

    [RelayCommand]
    private async Task StartPayment()
    {
        if (stored is null)
            return;
        PaymentAmountText = InvoicePayments.Due(stored).ToString("0.00", CultureInfo.CurrentCulture);
        PaymentDay = DateTime.Today;
        RecordingPayment = true;
        if (BankAccounts.Count > 0 || services.XeroApi is not { } api)
            return;
        try
        {
            foreach (var account in await api.BankAccountsAsync())
                BankAccounts.Add(account);
            PaymentAccount = BankAccounts.FirstOrDefault(a => a.Id == services.Settings.Xero.PaymentAccountId)
                ?? (BankAccounts.Count == 1 ? BankAccounts[0] : null);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ShowXero("Couldn't read the bank accounts from Xero: " + e.Message, problem: true);
        }
    }

    [RelayCommand]
    private void CancelPayment() => RecordingPayment = false;

    [RelayCommand]
    private Task RecordPayment()
    {
        if (stored is null)
            return Task.CompletedTask;
        var amount = Money.Parse(PaymentAmountText);
        DateOnly? day = PaymentDay is { } d ? DateOnly.FromDateTime(d) : null;
        if (InvoicePayments.Check(stored, amount, day, PaymentAccount?.Id, DateOnly.FromDateTime(DateTime.Today)) is { } problem)
        {
            ShowXero(problem, problem: true);
            return Task.CompletedTask;
        }
        var account = PaymentAccount!;
        services.Settings.Xero = services.Settings.Xero with { PaymentAccountId = account.Id };
        RecordingPayment = false;
        return Correct(async corrections =>
        {
            stored = await corrections.RecordPaymentAsync(stored!, amount!.Value, day!.Value, account.Id);
            return stored.Settled?.As == Settlement.Paid
                ? $"{Money.Rand(amount.Value)} recorded into {account.Name}. {stored.Xero.Number} is paid."
                : $"{Money.Rand(amount.Value)} recorded into {account.Name}. {Money.Rand(InvoicePayments.Due(stored))} is still owed.";
        });
    }
}
