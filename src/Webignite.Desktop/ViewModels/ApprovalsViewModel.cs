using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Everything waiting on Eddie: documents Claude or ChatGPT prepared, drafts in Xero not yet approved,
/// invoices approved but not emailed, and anything Xero left unclear. Opening one shows the usual editor.
/// </summary>
public partial class ApprovalsViewModel(AppServices services) : ViewModelBase
{
    public ObservableCollection<ApprovalRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial DocumentEditorViewModel? Editor { get; set; }

    public bool IsListing => Editor is null;
    public bool IsEmpty => Rows.Count == 0;
    public string Summary => Rows.Count switch
    {
        0 => "Nothing is waiting on you.",
        1 => "1 thing is waiting on you.",
        var n => $"{n} things are waiting on you.",
    };

    public void Refresh()
    {
        Rows.Clear();
        foreach (var item in Approvals.Waiting(services.Store.List()))
            Rows.Add(new ApprovalRowViewModel(item, this));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    internal void Open(ApprovalRowViewModel row)
    {
        if (services.Store.Get(row.Item.Stored.Id) is { } latest)
            Editor = new DocumentEditorViewModel(latest, services, () =>
            {
                Editor = null;
                Refresh();
            });
    }

    internal void Reject(ApprovalRowViewModel row)
    {
        // Only documents that never reached Xero can be thrown away here.
        if (row.Item.Kind == WaitingKind.FromAssistant && row.Item.Stored.XeroId is null)
            services.Store.DeleteDocument(row.Item.Stored.Id);
        Refresh();
    }
}

public partial class ApprovalRowViewModel(WaitingItem item, ApprovalsViewModel owner) : ViewModelBase
{
    public WaitingItem Item => item;
    public string What => item.Stored.Document.Kind == DocumentKind.Quote ? "Quote" : "Invoice";
    public bool IsQuote => item.Stored.Document.Kind == DocumentKind.Quote;
    public string Title => (item.Stored.XeroNumber is { } n ? n + "  ·  " : "")
        + (string.IsNullOrWhiteSpace(item.Stored.Document.Customer.Name) ? "No customer yet" : item.Stored.Document.Customer.Name);
    public string Total => Money.Rand(item.Stored.Document.Total ?? 0);
    public string Reason => item.Reason;
    public bool IsProblem => item.Kind == WaitingKind.XeroProblem;
    public bool CanReject => item.Kind == WaitingKind.FromAssistant;
    public string Lines => string.Join(", ", item.Stored.Document.Lines.Take(3).Select(l => l.Description))
        + (item.Stored.Document.Lines.Count > 3 ? $" and {item.Stored.Document.Lines.Count - 3} more" : "");

    [ObservableProperty]
    public partial bool ConfirmingReject { get; set; }

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void AskReject() => ConfirmingReject = true;

    [RelayCommand]
    private void KeepIt() => ConfirmingReject = false;

    [RelayCommand]
    private void Reject() => owner.Reject(this);
}
