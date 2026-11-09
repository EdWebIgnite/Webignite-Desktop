using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Xero;

namespace Webignite.Desktop.ViewModels;

/// <summary>Putting an invoice right in Xero: a credit note drafted there, or voiding it, only when clicked here.</summary>
public partial class DocumentEditorViewModel
{
    [ObservableProperty]
    public partial bool ConfirmingVoid { get; set; }

    [ObservableProperty]
    public partial bool Crediting { get; set; }

    [ObservableProperty]
    public partial string CreditAmountText { get; set; } = "";

    [ObservableProperty]
    public partial string CreditReason { get; set; } = "";

    public bool CanVoid => stored is not null && InvoiceCorrections.CanVoid(stored) && !XeroBusy && !ConfirmingVoid;
    public string VoidLabel => stored is null ? "" : InvoiceCorrections.VoidLabel(stored);
    public string VoidQuestion => stored is null ? "" : InvoiceCorrections.VoidQuestion(stored);
    public bool CanCredit => stored is not null && InvoiceCorrections.CanCredit(stored) && !XeroBusy && !Crediting;

    public string CreditHint => stored is null ? ""
        : $"Leave the amount empty to credit all {Money.Rand(InvoiceCorrections.Creditable(stored))} left. It goes into Xero as a draft for you to approve and allocate there.";

    public IReadOnlyList<string> CreditNotesText => stored is null ? [] : InvoiceCorrections.Describe(stored, TimeZoneInfo.Local);
    public bool HasCreditNotes => CreditNotesText.Count > 0;

    partial void OnConfirmingVoidChanged(bool value) => OnPropertyChanged(nameof(CanVoid));

    partial void OnCreditingChanged(bool value) => OnPropertyChanged(nameof(CanCredit));

    [RelayCommand]
    private void StartVoid() => ConfirmingVoid = true;

    [RelayCommand]
    private void CancelVoid() => ConfirmingVoid = false;

    [RelayCommand]
    private Task ConfirmVoid()
    {
        ConfirmingVoid = false;
        return Correct(async corrections =>
        {
            stored = await corrections.VoidAsync(stored!);
            return stored.Settled?.As == Settlement.Deleted ? "Deleted in Xero." : $"{stored.Xero.Number} is void in Xero and no longer owed.";
        });
    }

    [RelayCommand]
    private void StartCredit()
    {
        CreditAmountText = "";
        CreditReason = "";
        Crediting = true;
    }

    [RelayCommand]
    private void CancelCredit() => Crediting = false;

    [RelayCommand]
    private Task DraftCredit()
    {
        decimal? amount = null;
        if (!string.IsNullOrWhiteSpace(CreditAmountText))
        {
            amount = Money.Parse(CreditAmountText);
            if (amount is null)
            {
                ShowXero("Type the amount as a number, like 500 or 1 250.50.", problem: true);
                return Task.CompletedTask;
            }
        }
        if (stored is not null && InvoiceCorrections.CheckAmount(stored, amount) is { } problem)
        {
            ShowXero(problem, problem: true);
            return Task.CompletedTask;
        }
        if (services.Settings.Xero.AccountCode is not { } account)
        {
            ShowXero("Pick the sales account in Settings first.", problem: true);
            return Task.CompletedTask;
        }
        var reason = CreditReason;
        var target = new XeroTarget(account, BrandingThemeId: services.Settings.Xero.BrandingThemeId);
        Crediting = false;
        return Correct(async corrections =>
        {
            stored = await corrections.DraftCreditNoteAsync(stored!, amount, reason, target);
            var note = stored.CreditNotes[^1];
            return $"Credit note {note.Number} for {Money.Rand(note.Amount)} is a draft in Xero. Approve it there and allocate it to {stored.Xero.Number}.";
        });
    }

    private async Task Correct(Func<XeroCorrections, Task<string>> step)
    {
        if (stored is null || services.XeroCorrections is not { } corrections)
        {
            ShowXero("Connect Xero in Settings first.", problem: true);
            return;
        }
        stored = services.Documents.Get(stored.Id) ?? stored;
        XeroBusy = true;
        ShowXero("Talking to Xero…", problem: false);
        try
        {
            ShowXero(await step(corrections), problem: false);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            stored = services.Documents.Get(stored.Id) ?? stored;
            ShowXero("Couldn't do it in Xero: " + e.Message, problem: true);
        }
        finally
        {
            XeroBusy = false;
            RefreshXero();
        }
    }
}
