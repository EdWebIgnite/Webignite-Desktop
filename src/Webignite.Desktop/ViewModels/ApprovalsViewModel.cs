using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Everything waiting on Eddie: documents Claude or ChatGPT prepared, drafts in Xero not yet approved,
/// invoices approved but not emailed, and anything Xero left unclear. A card at the top of Overview, there only
/// while something waits; opening one shows it in Quotes or Invoices, in the usual editor.
/// </summary>
public partial class ApprovalsViewModel(AppServices services) : ViewModelBase, IRefreshable
{
    public ObservableCollection<ApprovalRowViewModel> Rows { get; } = [];

    /// <summary>Told how many quotes and how many invoices wait, each time the list is read: the sidebar shows them on Quotes and Invoices.</summary>
    public Action<int, int>? Counted { get; set; }

    public bool IsEmpty => Rows.Count == 0;
    public bool HasRows => Rows.Count > 0;
    public string Summary => Rows.Count switch
    {
        0 => "Nothing is waiting on you.",
        1 => "1 thing is waiting on you.",
        var n => $"{n} things are waiting on you.",
    };

    public void Refresh()
    {
        Rows.Clear();
        foreach (var item in Approvals.Waiting(services.Documents.List()))
            Rows.Add(new ApprovalRowViewModel(item, this));
        Counted?.Invoke(Rows.Count(r => r.IsQuote), Rows.Count(r => !r.IsQuote));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(Summary));
    }

    internal void Open(ApprovalRowViewModel row)
    {
        if (services.Documents.Get(row.Item.Stored.Id) is { } latest)
            services.Go.Document(latest);
    }

    internal void Remove(ApprovalRowViewModel row)
    {
        if (services.Documents.TakeOffApprovals(row.Item, DateTimeOffset.UtcNow) is not { } off)
            return;
        Refresh();
        var name = off.Xero.Number ?? (off.Document.Kind == DocumentKind.Quote ? "the quote" : "the invoice");
        services.OfferUndo($"Took {name} off To approve", () =>
        {
            services.Documents.PutBackOnApprovals(off.Id);
            Refresh();
        });
    }

    internal void Reject(ApprovalRowViewModel row)
    {
        // Only documents that never reached Xero can be thrown away here.
        if (services.Documents.Reject(row.Item) is not { } gone)
            return;
        Refresh();
        services.OfferUndo($"Rejected the {(gone.Document.Kind == DocumentKind.Quote ? "quote" : "invoice")} for {gone.Document.Customer.Name}", () =>
        {
            services.Documents.Restore(gone);
            Refresh();
        });
    }
}

public partial class ApprovalRowViewModel(WaitingItem item, ApprovalsViewModel owner) : ViewModelBase
{
    public WaitingItem Item => item;
    public string What => item.Stored.Document.Kind == DocumentKind.Quote ? "Quote" : "Invoice";
    public bool IsQuote => item.Stored.Document.Kind == DocumentKind.Quote;
    public string Title => (item.Stored.Xero.Number is { } n ? n + "  ·  " : "")
        + (string.IsNullOrWhiteSpace(item.Stored.Document.Customer.Name) ? "No customer yet" : item.Stored.Document.Customer.Name);
    public string Total => Money.Rand(item.Stored.Document.Total ?? 0);
    public string Reason => item.Reason;
    public bool IsProblem => item.Kind == WaitingKind.XeroProblem;
    public bool CanReject => Approvals.CanReject(item);
    public bool CanRemove => Approvals.CanRemove(item);
    public bool ShowsButtons => !ConfirmingReject && !ConfirmingRemove;
    public string Lines => string.Join(", ", item.Stored.Document.Lines.Take(3).Select(l => l.Description))
        + (item.Stored.Document.Lines.Count > 3 ? $" and {item.Stored.Document.Lines.Count - 3} more" : "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsButtons))]
    public partial bool ConfirmingReject { get; set; }

    /// <summary>Asking whether to take it off the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsButtons))]
    public partial bool ConfirmingRemove { get; set; }

    [RelayCommand]
    private void AskRemove() => ConfirmingRemove = true;

    [RelayCommand]
    private void KeepOn() => ConfirmingRemove = false;

    [RelayCommand]
    private void Remove() => owner.Remove(this);

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void AskReject() => ConfirmingReject = true;

    [RelayCommand]
    private void KeepIt() => ConfirmingReject = false;

    [RelayCommand]
    private void Reject() => owner.Reject(this);
}
