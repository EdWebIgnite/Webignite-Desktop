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

    /// <summary>Nothing saved at all, so the page shows how to start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAny))]
    public partial bool IsEmpty { get; set; }

    public bool HasAny => !IsEmpty;

    /// <summary>There are some, but the search and filters hide them all.</summary>
    public bool NoMatches => !IsEmpty && Rows.Count == 0;

    public bool ShowTable => Rows.Count > 0;

    public static IReadOnlyList<Choice<DocumentShow>> ShowChoices { get; } =
    [
        new("Everything", DocumentShow.All),
        new("Not in Xero yet", DocumentShow.NotInXero),
        new("In Xero", DocumentShow.InXero),
    ];

    public static IReadOnlyList<Choice<DocumentPeriod>> PeriodChoices { get; } =
    [
        new("Any date", DocumentPeriod.AllTime),
        new("This month", DocumentPeriod.ThisMonth),
        new("Last month", DocumentPeriod.LastMonth),
        new("This tax year", DocumentPeriod.ThisTaxYear),
    ];

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    public partial Choice<DocumentShow> ShowChoice { get; set; } = ShowChoices[0];

    [ObservableProperty]
    public partial Choice<DocumentPeriod> PeriodChoice { get; set; } = PeriodChoices[0];

    /// <summary>"3 of 12, R 9 450.00" when filtered, so the total of what's showing is always to hand.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    public bool IsFiltered => Filter.IsActive;

    private DocumentFilter Filter => new(Search, ShowChoice?.Value ?? DocumentShow.All, PeriodChoice?.Value ?? DocumentPeriod.AllTime);

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnShowChoiceChanged(Choice<DocumentShow> value) => Refresh();
    partial void OnPeriodChoiceChanged(Choice<DocumentPeriod> value) => Refresh();

    [RelayCommand]
    private void ClearFilters()
    {
        Search = "";
        ShowChoice = ShowChoices[0];
        PeriodChoice = PeriodChoices[0];
    }

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
        var all = services.Store.List(Kind);
        var shown = Filter.Apply(all, DateOnly.FromDateTime(DateTime.Today));
        Rows.Clear();
        foreach (var stored in shown)
            Rows.Add(new DocumentRow(stored));
        IsEmpty = all.Count == 0;
        var total = Money.Rand(shown.Sum(d => d.Document.Total ?? 0));
        Summary = Filter.IsActive ? $"{shown.Count} of {all.Count}, {total}" : $"{all.Count} in all, {total}";
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(ShowTable));
        OnPropertyChanged(nameof(IsFiltered));
    }
}
