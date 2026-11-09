using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class DocumentEditorView : UserControl
{
    public DocumentEditorView() => InitializeComponent();

    /// <summary>Picks a file in the app's own explorer and attaches it to the quote or invoice in Xero.</summary>
    private void PickAttachment(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DocumentEditorViewModel editor)
            Attach(editor).Forget("Attaching a file in Xero", tell: true);
    }

    private async Task Attach(DocumentEditorViewModel editor)
    {
        var picked = await Picker.Files(this, "Pick the file to attach in Xero");
        if (picked.Count > 0)
            await editor.AttachAsync(picked[0]);
    }
}
