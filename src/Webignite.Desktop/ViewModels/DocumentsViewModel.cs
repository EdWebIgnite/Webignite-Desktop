using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>The Quotes or Invoices module: a list, and the editor when one is open.</summary>
public partial class DocumentsViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices services;

    public DocumentsViewModel(DocumentKind kind, AppServices services)
    {
        Kind = kind;
        this.services = services;
        ShowChips = ShowChoices(kind).Select(c => new FilterChip(c.Label, () => Show = c.Value) { IsOn = c.Value == DocumentShow.All }).ToList();
        Refresh();
    }

    public DocumentKind Kind { get; }
    public string Title => Kind == DocumentKind.Quote ? "Quotes" : "Invoices";
    public string NewLabel => Kind == DocumentKind.Quote ? "New quote" : "New invoice";
    public string EmptyTitle => Kind == DocumentKind.Quote ? "No quotes yet" : "No invoices yet";
    public string EmptyText => "Start one here, or ask Claude or ChatGPT to prepare one. It waits on this PC until you send it to Xero.";
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

    /// <summary>The quick filters above the list. Quotes are never overdue, and are accepted rather than paid.</summary>
    public static IReadOnlyList<Choice<DocumentShow>> ShowChoices(DocumentKind kind) => kind == DocumentKind.Quote
        ?
        [
            new("All", DocumentShow.All),
            new("Not in Xero yet", DocumentShow.NotInXero),
            new("In Xero", DocumentShow.InXero),
            new("Accepted", DocumentShow.Settled),
        ]
        :
        [
            new("All", DocumentShow.All),
            new("Not in Xero yet", DocumentShow.NotInXero),
            new("In Xero", DocumentShow.InXero),
            new("Overdue", DocumentShow.Overdue),
            new("Paid", DocumentShow.Settled),
        ];

    public IReadOnlyList<FilterChip> ShowChips { get; }

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
    public partial DocumentShow Show { get; set; } = DocumentShow.All;

    [ObservableProperty]
    public partial Choice<DocumentPeriod> PeriodChoice { get; set; } = PeriodChoices[0];

    /// <summary>"3 of 12, R 9 450.00" when filtered, so the total of what's showing is always to hand.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    /// <summary>The line under the title: "4 invoices, R 5 600.00 owed, 1 overdue".</summary>
    [ObservableProperty]
    public partial string Headline { get; set; } = "";

    public bool IsFiltered => Filter.IsActive;

    /// <summary>The line under a short list, or null when the list is long, filtered or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    public partial DocumentHint? Hint { get; set; }

    public bool HasHint => Hint is not null;

    /// <summary>Does what the hint offers: shows the filter it names, or starts a new one.</summary>
    [RelayCommand]
    private void FollowHint()
    {
        if (Hint?.Show is { } show)
            Show = show;
        else
            New();
    }

    private DocumentFilter Filter => new(Search, Show, PeriodChoice?.Value ?? DocumentPeriod.AllTime);

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnShowChanged(DocumentShow value)
    {
        var choices = ShowChoices(Kind);
        for (var i = 0; i < ShowChips.Count; i++)
            ShowChips[i].IsOn = choices[i].Value == value;
        Refresh();
    }
    partial void OnPeriodChoiceChanged(Choice<DocumentPeriod> value) => Refresh();

    [RelayCommand]
    private void ClearFilters()
    {
        Search = "";
        Show = DocumentShow.All;
        PeriodChoice = PeriodChoices[0];
    }

    /// <summary>The open document, or null while the list is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial DocumentEditorViewModel? Editor { get; set; }

    // Whatever replaces an open editor, what was typed in it is saved first.
    partial void OnEditorChanging(DocumentEditorViewModel? oldValue, DocumentEditorViewModel? newValue) => oldValue?.Keep();

    public bool IsListing => Editor is null;

    [RelayCommand]
    private void New() => Editor = new DocumentEditorViewModel(Kind, services, Close);

    [RelayCommand]
    private void Open(DocumentRow row) => Editor = new DocumentEditorViewModel(row.Stored, services, Close);

    /// <summary>Opens a quote or invoice, e.g. from search.</summary>
    public void OpenDocument(StoredDocument stored) => Editor = new DocumentEditorViewModel(services.Documents.Get(stored.Id) ?? stored, services, Close);

    private void Close()
    {
        Editor = null;
        Refresh();
    }

    public void Refresh()
    {
        var all = services.Documents.List(Kind);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var shown = Filter.Apply(all, today);
        Rows.Clear();
        foreach (var stored in shown)
            Rows.Add(new DocumentRow(stored, today));
        IsEmpty = all.Count == 0;
        Headline = DocumentHeadline.For(Kind, all, today);
        Hint = ListHints.Shows(Rows.Count, Filter.IsActive) ? DocumentHeadline.Hint(Kind, all, today) : null;
        Summary = Filter.IsActive ? $"{shown.Count} of {all.Count}, {Money.Rand(shown.Sum(d => d.Document.Total ?? 0))}" : "";
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(ShowTable));
        OnPropertyChanged(nameof(IsFiltered));
    }
}
