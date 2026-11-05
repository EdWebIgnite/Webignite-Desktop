using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ContactsView : FullBleedView
{
    public ContactsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ContactsViewModel contacts)
            contacts.CopyText = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            };
    }

    private void GroupPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ContactsViewModel contacts && GroupBox.SelectedItem is string group && group != contacts.Group)
            contacts.PickGroup(group);
    }

    // One-way, so a new list of clients arriving never changes the person's client; only a real pick does.
    private void ClientPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ContactsViewModel contacts && ClientBox.SelectedItem is Choice<Webignite.Core.Clients.Client?> choice && choice != contacts.EditClient)
            contacts.PickClient(choice);
    }

    private void ImportClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ImportAsync().Forget("Importing contacts", tell: true);

    private async Task ImportAsync()
    {
        if (DataContext is not ContactsViewModel contacts)
            return;
        if ((await Files.Picker.Files(this, "Import people", ["*.vcf", "*.csv"], kindName: "vCard or CSV files")).FirstOrDefault() is { } path)
            await contacts.ImportAsync(path);
    }

    private void ExportVCardClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Export(ContactFileKind.VCard, false);
    private void ExportCsvClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Export(ContactFileKind.Csv, false);
    private void ExportGroupVCardClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Export(ContactFileKind.VCard, true);
    private void ExportGroupCsvClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Export(ContactFileKind.Csv, true);

    private void Export(ContactFileKind kind, bool groupOnly) => ExportAsync(kind, groupOnly).Forget("Exporting contacts", tell: true);

    private async Task ExportAsync(ContactFileKind kind, bool groupOnly)
    {
        if (DataContext is not ContactsViewModel contacts)
            return;
        var patterns = kind == ContactFileKind.Csv ? new[] { "*.csv" } : ["*.vcf"];
        if (await Files.Picker.Save(this, "Export people", contacts.ExportName(kind, groupOnly), patterns,
                kind == ContactFileKind.Csv ? "CSV file" : "vCard file") is { } path)
            await contacts.ExportAsync(path, kind, groupOnly);
    }

    private void PaneResized(string pane, double width) => (DataContext as ContactsViewModel)?.Panels.Resized(pane, width);
}
