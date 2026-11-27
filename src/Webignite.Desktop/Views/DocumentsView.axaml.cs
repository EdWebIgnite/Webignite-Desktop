using Avalonia.Controls;

namespace Webignite.Desktop.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView() => InitializeComponent();

    private void ImportClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => PickImportAsync().Forget("Picking a spreadsheet of old quotes and invoices", tell: true);

    private async Task PickImportAsync()
    {
        if (DataContext is Webignite.Desktop.ViewModels.DocumentsViewModel documents
            && (await Files.Picker.Files(this, "Old quotes and invoices", ["*.csv"], kindName: "CSV files")).FirstOrDefault() is { } path)
            documents.LookAtFile(path);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is Webignite.Desktop.ViewModels.DocumentsViewModel documents)
            documents.CopyText = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            };
    }
}
