using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>The Quotes or Invoices module: a list, and the editor when one is open.</summary>
public partial class DocumentsViewModel : ViewModelBase
{
    private readonly AppServices services;

    public DocumentsViewModel(DocumentKind kind, AppServices services)
    {
        Kind = kind;
        this.services = services;
        Refresh();
    }

    public DocumentKind Kind { get; }
    public string Title => Kind == DocumentKind.Quote ? "Quotes" : "Invoices";
    public string NewLabel => Kind == DocumentKind.Quote ? "New quote" : "New invoice";
    public string EmptyTitle => Kind == DocumentKind.Quote ? "No quotes yet" : "No invoices yet";
    public string EmptyText => Kind == DocumentKind.Quote
        ? "Start one here, or ask Claude or ChatGPT to prepare one. It waits on this PC until you send it to Xero."
        : "Start one here, or ask Claude or ChatGPT to prepare one. It waits on this PC until you send it to Xero.";
    public string EmptyIcon => Kind == DocumentKind.Quote ? "Quotes" : "Invoices";

    public ObservableCollection<DocumentRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The open document, or null while the list is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial DocumentEditorViewModel? Editor { get; set; }

    public bool IsListing => Editor is null;

    [RelayCommand]
    private void New() => Editor = new DocumentEditorViewModel(Kind, services, Close);

    [RelayCommand]
    private void Open(DocumentRow row) => Editor = new DocumentEditorViewModel(row.Stored, services, Close);

    /// <summary>Opens a quote or invoice, e.g. from search.</summary>
    public void OpenDocument(StoredDocument stored) => Editor = new DocumentEditorViewModel(services.Store.Get(stored.Id) ?? stored, services, Close);

    private void Close()
    {
        Editor = null;
        Refresh();
    }

    public void Refresh()
    {
        Rows.Clear();
        foreach (var stored in services.Store.List(Kind))
            Rows.Add(new DocumentRow(stored));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
