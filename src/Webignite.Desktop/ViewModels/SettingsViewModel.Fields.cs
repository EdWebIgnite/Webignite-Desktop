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

    private void LoadFields()
    {
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
