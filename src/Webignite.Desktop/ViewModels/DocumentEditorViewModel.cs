using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Writes a quote or invoice by hand. Totals, dates and checks come from Webignite.Core, the same
/// rules the assistants' documents go through, so what you see here is what Xero will get.
/// </summary>
public partial class DocumentEditorViewModel : ViewModelBase
{
    private readonly DocumentSettings settings;

    public DocumentEditorViewModel(DocumentKind kind, DocumentSettings settings)
    {
        Kind = kind;
        this.settings = settings;
        Lines.Add(new LineRowViewModel(Recalculate));
        Recalculate();
    }

    public DocumentKind Kind { get; }
    public string Title => Kind == DocumentKind.Quote ? "New quote" : "New invoice";
    public string DueLabel => Kind == DocumentKind.Quote ? "Expires" : "Due";
    public string NumberNote => "Xero gives it the next number when it is created.";

    public ObservableCollection<LineRowViewModel> Lines { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty]
    public partial string CustomerName { get; set; } = "";

    [ObservableProperty]
    public partial string Reference { get; set; } = "";

    [ObservableProperty]
    public partial string Notes { get; set; } = "";

    [ObservableProperty]
    public partial string Subtotal { get; set; } = "";

    [ObservableProperty]
    public partial string Total { get; set; } = "";

    [ObservableProperty]
    public partial string DateText { get; set; } = "";

    [ObservableProperty]
    public partial string DueText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    partial void OnCustomerNameChanged(string value) => Recalculate();

    partial void OnReferenceChanged(string value) => Recalculate();

    partial void OnNotesChanged(string value) => Recalculate();

    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(new LineRowViewModel(Recalculate));
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(LineRowViewModel row)
    {
        Lines.Remove(row);
        if (Lines.Count == 0)
            Lines.Add(new LineRowViewModel(Recalculate));
        Recalculate();
    }

    public Document Build() => DocumentDefaults.Complete(new Document
    {
        Kind = Kind,
        Origin = DocumentOrigin.New,
        Customer = new Customer { Name = CustomerName.Trim() },
        Reference = string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim(),
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        Lines = Lines.Select(row => row.ToLine()).OfType<Line>().ToList(),
    }, settings, DateOnly.FromDateTime(DateTime.Today));

    private void Recalculate()
    {
        var doc = Build();
        Subtotal = Money.Rand(doc.Subtotal ?? 0);
        Total = Money.Rand(doc.Total ?? 0);
        DateText = doc.Date?.ToString("d MMM yyyy") ?? "";
        DueText = doc.DueOrExpiryDate?.ToString("d MMM yyyy") ?? "";

        Problems.Clear();
        foreach (var problem in DocumentValidator.Validate(doc))
            Problems.Add(problem);
        IsReady = Problems.Count == 0;
    }
}
