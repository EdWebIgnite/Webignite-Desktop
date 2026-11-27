using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

// Fields of Eddie's own on quotes and invoices (Project to begin with): add one, say whether it prints, take one off.
// Shared with the other PC, as part of the document settings.
public partial class SettingsViewModel
{
    public ObservableCollection<FieldSettingRow> DocumentFieldRows { get; } = [];

    [ObservableProperty]
    public partial string NewFieldName { get; set; } = "";

    [ObservableProperty]
    public partial string FieldMessage { get; set; } = "";

    /// <summary>Quotes and invoices are made final in Webignite, not Xero. Saved, and shared with the other PC, as soon as it's ticked.</summary>
    public bool MakeInWebignite
    {
        get => services.Settings.Documents.MakeInWebignite;
        set
        {
            if (value == services.Settings.Documents.MakeInWebignite)
                return;
            services.Settings.Documents = services.Settings.Documents with { MakeInWebignite = value };
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial string NextQuoteNumber { get; set; } = "";

    [ObservableProperty]
    public partial string NextInvoiceNumber { get; set; } = "";

    [ObservableProperty]
    public partial string NumbersMessage { get; set; } = "";

    // What each kind gets next: the number set here, or one past the highest used, Xero's included.
    private void ShowNextNumbers()
    {
        var documents = services.Documents.List();
        var settings = services.Settings.Documents;
        NextQuoteNumber = OwnInvoicing.NextNumber(DocumentKind.Quote, documents, settings.NextQuoteNumber);
        NextInvoiceNumber = OwnInvoicing.NextNumber(DocumentKind.Invoice, documents, settings.NextInvoiceNumber);
    }

    /// <summary>Keeps the next numbers as typed. One already used or behind moves on past the highest used.</summary>
    [RelayCommand]
    private void SaveNextNumbers()
    {
        if (!NextQuoteNumber.Trim().Any(char.IsAsciiDigit) || !NextInvoiceNumber.Trim().Any(char.IsAsciiDigit) || !char.IsAsciiDigit(NextQuoteNumber.Trim()[^1]) || !char.IsAsciiDigit(NextInvoiceNumber.Trim()[^1]))
        {
            NumbersMessage = "End each number with digits, like QU-1025 or INV-0200.";
            return;
        }
        var wanted = (Quote: NextQuoteNumber.Trim(), Invoice: NextInvoiceNumber.Trim());
        services.Settings.Documents = services.Settings.Documents with { NextQuoteNumber = wanted.Quote, NextInvoiceNumber = wanted.Invoice };
        ShowNextNumbers();
        NumbersMessage = NextQuoteNumber == wanted.Quote && NextInvoiceNumber == wanted.Invoice
            ? $"Saved. The next ones are {NextQuoteNumber} and {NextInvoiceNumber}."
            : $"Saved, but a number was already used, so the next ones are {NextQuoteNumber} and {NextInvoiceNumber}.";
    }

    private void LoadFields()
    {
        OnPropertyChanged(nameof(MakeInWebignite));
        ShowNextNumbers();
        DocumentFieldRows.Clear();
        foreach (var field in services.Settings.Documents.Fields)
            DocumentFieldRows.Add(new FieldSettingRow(field, this));
    }

    [RelayCommand]
    private void AddField()
    {
        var offered = services.Settings.Documents.Fields;
        if (DocumentFields.Problem(offered, NewFieldName) is { } problem)
        {
            FieldMessage = problem;
            return;
        }
        SaveFields([.. offered, new FieldDefinition(NewFieldName.Trim())]);
        FieldMessage = $"Added. New quotes and invoices have {NewFieldName.Trim()}.";
        NewFieldName = "";
    }

    internal void ChangeField(FieldDefinition field, FieldDefinition changed) =>
        SaveFields([.. services.Settings.Documents.Fields.Select(f => f == field ? changed : f)]);

    internal void RemoveField(FieldDefinition field)
    {
        SaveFields([.. services.Settings.Documents.Fields.Where(f => f != field)]);
        FieldMessage = $"Took {field.Name} off. Quotes and invoices that have it keep it.";
    }

    private void SaveFields(IReadOnlyList<FieldDefinition> fields)
    {
        services.Settings.Documents = services.Settings.Documents with { Fields = fields };
        LoadFields();
    }
}

/// <summary>One field in Settings: its name, whether it prints for the client, and Remove.</summary>
public sealed partial class FieldSettingRow : ObservableObject
{
    private readonly FieldDefinition definition;
    private readonly SettingsViewModel owner;

    public FieldSettingRow(FieldDefinition field, SettingsViewModel owner)
    {
        definition = field;
        this.owner = owner;
        print = field.Print;
    }

    public string Name => definition.Name;

    private bool print;

    /// <summary>Printed on the quote or invoice the client gets; off keeps it for us only.</summary>
    public bool Print
    {
        get => print;
        set
        {
            if (SetProperty(ref print, value))
                owner.ChangeField(definition, definition with { Print = value });
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemoveField(definition);
}
