using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Writes a quote or invoice by hand. Totals, dates and checks come from Webignite.Core, the same
/// rules the assistants' documents go through, so what you see here is what Xero will get.
/// </summary>
public partial class DocumentEditorViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly Action close;
    private StoredDocument? stored;
    private bool loading;
    private bool changed; // typed into since it was opened or last saved
    private readonly DocumentSettings documentSettings; // read once: Build runs on every key

    public DocumentEditorViewModel(DocumentKind kind, AppServices services, Action close)
    {
        Kind = kind;
        this.services = services;
        this.close = close;
        documentSettings = services.Settings.Documents;
        Lines.Add(NewRow());
        Recalculate();
        changed = false;
    }

    public DocumentEditorViewModel(StoredDocument stored, AppServices services, Action close)
        : this(stored.Document.Kind, services, close)
    {
        this.stored = stored;
        var doc = stored.Document;
        loading = true;
        CustomerName = doc.Customer.Name;
        Reference = doc.Reference ?? "";
        Notes = doc.Notes ?? "";
        Lines.Clear();
        foreach (var line in doc.Lines)
            Lines.Add(LineRowViewModel.From(line, Recalculate));
        if (Lines.Count == 0)
            Lines.Add(NewRow());
        loading = false;
        Recalculate();
        changed = false;
    }

    public DocumentKind Kind { get; }
    public string Title => stored?.Xero.Number ?? (stored is null ? (Kind == DocumentKind.Quote ? "New quote" : "New invoice") : (Kind == DocumentKind.Quote ? "Draft quote" : "Draft invoice"));
    public string DueLabel => Kind == DocumentKind.Quote ? "Expires" : "Due";
    public string NumberNote =>
        stored?.Xero.Number is not null ? "Created in Xero. Changes are made in Xero from here on."
        : stored is { Status: DocumentStatus.AwaitingApproval } ? $"{(stored.Source == DocumentSource.ChatGpt ? "ChatGPT" : "Claude")} prepared this. Check it, change anything, then create it in Xero."
        : "Xero gives it the next number when it is created.";

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

    [ObservableProperty]
    public partial string SaveState { get; set; } = "";

    partial void OnCustomerNameChanged(string value) => Recalculate();

    partial void OnReferenceChanged(string value) => Recalculate();

    partial void OnNotesChanged(string value) => Recalculate();

    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(NewRow());
        Recalculate();
    }

    /// <summary>Puts an amount from the calculator on the first empty line, or a new one. False when it can't be changed.</summary>
    public bool AddAmount(decimal amount, string description)
    {
        if (!IsEditable)
            return false;
        var row = Lines.FirstOrDefault(r => r.IsBlank);
        if (row is null)
        {
            row = NewRow();
            Lines.Add(row);
        }
        row.Description = description;
        row.Quantity = "1";
        row.Price = Webignite.Core.Tools.Calculator.Plain(Money.Cents(amount));
        Recalculate();
        return true;
    }

    [RelayCommand]
    private void RemoveLine(LineRowViewModel row)
    {
        Lines.Remove(row);
        if (Lines.Count == 0)
            Lines.Add(NewRow());
        Recalculate();
    }

    public bool IsQuote => Kind == DocumentKind.Quote;

    /// <summary>Copy and Make invoice need something to copy.</summary>
    public bool CanCopy => HasContent;

    /// <summary>Starts a new draft of the same kind with this one's customer, lines and notes.</summary>
    [RelayCommand]
    private void Copy() => OpenCopy(Kind);

    /// <summary>Starts a draft invoice from this quote, for when the client says yes.</summary>
    [RelayCommand]
    private void MakeInvoice() => OpenCopy(DocumentKind.Invoice);

    private void OpenCopy(DocumentKind kind)
    {
        if (!HasContent)
            return;
        if (IsEditable)
            Save();
        var source = stored ?? new StoredDocument { Id = StoredDocument.NewId(), Document = Build() };
        var copy = services.Documents.Copy(source, kind, services.Settings.Documents, DateOnly.FromDateTime(DateTime.Today));
        services.Go.Document(copy);
    }

    /// <summary>Saves to this PC. Works even when the document isn't finished yet.</summary>
    [RelayCommand]
    private void Save()
    {
        if (!IsEditable)
            return; // Once it has gone to Xero, Xero holds the real copy.
        // Saved onto the document as it is now, and only while it still hasn't gone to Xero. An editor left open
        // on another page mustn't wipe a Xero link made from here meanwhile, or the next Create makes a second one.
        var latest = services.Documents.SaveDraft(stored?.Id ?? StoredDocument.NewId(), Build());
        if (latest is { Xero.State: not XeroWriteState.None })
        {
            stored = latest;
            RefreshXero();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(NumberNote));
            SaveState = "Not saved: it went to Xero from another page while this was open, so Xero holds it now.";
            return;
        }
        stored = latest;
        changed = false;
        SaveState = $"Saved on this PC at {stored.Updated.ToLocalTime():HH:mm}";
        SavedStamp++;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Saves what was typed before the editor goes away for any reason: another document opened over it, another
    /// client picked, or the app closing. Back already saves; this covers every other way out.
    /// </summary>
    public void Keep()
    {
        if (changed && HasContent)
            Save();
    }

    /// <summary>Counts up on each save, so the short "Saved" note beside the Save button shows again.</summary>
    [ObservableProperty]
    public partial int SavedStamp { get; set; }

    [RelayCommand]
    private void Back()
    {
        if (HasContent)
            Save();
        close();
    }

    private bool HasContent => !string.IsNullOrWhiteSpace(CustomerName) || Lines.Any(row => !row.IsBlank) || stored is not null;

    public Document Build() => DocumentDefaults.Complete(new Document
    {
        Kind = Kind,
        Origin = stored?.Document.Origin ?? DocumentOrigin.New,
        // Keep the email and address an assistant or import supplied; only the name is edited here.
        Customer = (stored?.Document.Customer ?? new Customer { Name = "" }) with { Name = CustomerName.Trim() },
        Reference = string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim(),
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        Date = stored?.Document.Date,
        DueOrExpiryDate = stored?.Document.DueOrExpiryDate,
        Lines = Lines.Select(row => row.ToLine()).OfType<Line>().ToList(),
    }, documentSettings, DateOnly.FromDateTime(DateTime.Today));

    private LineRowViewModel NewRow() => new(Recalculate);

    private void Recalculate()
    {
        if (loading)
            return;
        changed = true;
        var doc = Build();
        Subtotal = Money.Rand(doc.Subtotal ?? 0);
        Total = Money.Rand(doc.Total ?? 0);
        DateText = doc.Date?.ToString("d MMM yyyy") ?? "";
        DueText = doc.DueOrExpiryDate?.ToString("d MMM yyyy") ?? "";
        SaveState = "";

        OnPropertyChanged(nameof(CanCopy));
        Problems.Clear();
        foreach (var problem in DocumentValidator.Validate(doc))
            Problems.Add(problem);
        IsReady = Problems.Count == 0;
    }
}
