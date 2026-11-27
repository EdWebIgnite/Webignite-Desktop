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

    public bool CanVoid => stored is not null && (InvoiceCorrections.CanVoid(stored) || OwnInvoicing.CanVoid(stored)) && !XeroBusy && !ConfirmingVoid;
    public string VoidLabel => stored is null ? "" : IsIssued ? "Void" : InvoiceCorrections.VoidLabel(stored);
    public string VoidQuestion => stored is null ? "" : IsIssued ? OwnInvoicing.VoidQuestion(stored) : InvoiceCorrections.VoidQuestion(stored);
    public bool CanCredit => stored is not null && (InvoiceCorrections.CanCredit(stored) || OwnInvoicing.CanCredit(stored)) && !XeroBusy && !Crediting;

    public string CreditHint => stored is null ? ""
        : IsIssued ? $"Leave the amount empty to credit all {Money.Rand(OwnInvoicing.Due(stored))} still owed. It gets the next credit note number and takes the amount off what's owed."
        : $"Leave the amount empty to credit all {Money.Rand(InvoiceCorrections.Creditable(stored))} left. It goes into Xero as a draft for you to approve and allocate there.";

    public string CreditButtonText => IsIssued ? "Make the credit note" : "Draft credit note in Xero";
    public string CreditTitle => IsIssued ? "Make a credit note" : "Draft a credit note in Xero";
    public string CreditStartText => IsIssued ? "Make a credit note" : "Draft a credit note";

    public IReadOnlyList<string> CreditNotesText => stored is null ? []
        : IsIssued ? [.. stored.Own!.Credits.Select(c => $"Credit note {c.Number}, {c.Date:d MMM yyyy}: {Money.Rand(c.Amount)}" + (c.Reason is { } why ? $", {why}" : ""))]
        : InvoiceCorrections.Describe(stored, TimeZoneInfo.Local);
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
        if (IsIssued)
        {
            OwnChange(s => OwnInvoicing.Void(s, DateTimeOffset.Now), s => $"{s.Own!.Number} is void and no longer owed.");
            return Task.CompletedTask;
        }
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
        if (IsIssued)
        {
            if (stored is not null && OwnInvoicing.CheckCredit(stored, amount) is { } ownProblem)
            {
                ShowXero(ownProblem, problem: true);
                return Task.CompletedTask;
            }
            var why = CreditReason;
            var all = services.Documents.List();
            Crediting = false;
            OwnChange(s => OwnInvoicing.Credit(s, amount, why, DateOnly.FromDateTime(DateTime.Today), all, DateTimeOffset.Now),
                s => $"Credit note {s.Own!.Credits[^1].Number} for {Money.Rand(s.Own.Credits[^1].Amount)} is made. "
                    + (OwnInvoicing.Due(s) > 0 ? $"{Money.Rand(OwnInvoicing.Due(s))} is still owed." : "Nothing is owed now."));
            return Task.CompletedTask;
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

    /// <summary>A file was picked to attach: show it on the online invoice too when this is ticked.</summary>
    [ObservableProperty]
    public partial bool AttachOnline { get; set; }

    public bool CanAttach => stored is not null && DocumentAttachments.CanAttach(stored) && !XeroBusy;
    public IReadOnlyList<string> AttachmentsText => stored is null ? [] : DocumentAttachments.Describe(stored);
    public bool HasAttachments => AttachmentsText.Count > 0;

    /// <summary>Attaches the file picked on the page to the quote or invoice in Xero.</summary>
    public Task AttachAsync(string path) => Correct(async corrections =>
    {
        var name = Path.GetFileName(path);
        var info = new FileInfo(path);
        if (DocumentAttachments.Check(name, info.Length) is { } problem)
            throw new XeroException(problem);
        var content = await File.ReadAllBytesAsync(path);
        stored = await corrections.AttachAsync(stored!, name, content, AttachOnline);
        return Kind == DocumentKind.Invoice && AttachOnline
            ? $"{name} is attached in Xero and shown on the online invoice."
            : $"{name} is attached in Xero.";
    });

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
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or UnauthorizedAccessException)
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
