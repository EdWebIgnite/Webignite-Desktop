using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

// Fields of Eddie's own on the quote or invoice (Project to begin with), as set up in Settings.
public partial class DocumentEditorViewModel
{
    public ObservableCollection<DocumentFieldRow> FieldRows { get; } = [];
    public bool HasFields => FieldRows.Count > 0;

    private void LoadFields(Document doc)
    {
        FieldRows.Clear();
        foreach (var (field, value) in DocumentFields.ToFill(documentSettings.Fields, doc))
            FieldRows.Add(new DocumentFieldRow(field.Name, value, field.Print, Recalculate));
        OfferProjects();
        OnPropertyChanged(nameof(HasFields));
    }

    // The client's projects to pick from in Project, which follow the customer typed.
    private void OfferProjects()
    {
        if (FieldRows.FirstOrDefault(r => r.Name.Equals(DocumentFields.Project, StringComparison.OrdinalIgnoreCase)) is not { } row)
            return;
        var client = services.Clients.Find(CustomerName);
        row.Choices = DocumentFields.ProjectChoices(client, client is null ? [] : services.Projects.Projects());
    }

    private IReadOnlyList<DocumentField> TypedFields() =>
        [.. FieldRows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => new DocumentField(r.Name, r.Value.Trim(), r.Print))];
}

/// <summary>One field of Eddie's own on the editor, e.g. Project, with names to pick from where there are some.</summary>
public sealed partial class DocumentFieldRow(string name, string value, bool print, Action changed) : ObservableObject
{
    public string Name { get; } = name;
    public bool Print { get; } = print;
    public string Tip => Print ? "Printed on the quote or invoice" : "For us only: not printed";

    [ObservableProperty]
    public partial string Value { get; set; } = value;

    [ObservableProperty]
    public partial IReadOnlyList<string> Choices { get; set; } = [];

    public string Placeholder => Name == DocumentFields.Project ? "Which project, e.g. the end client's site" : "Optional";

    partial void OnValueChanged(string value) => changed();
}
