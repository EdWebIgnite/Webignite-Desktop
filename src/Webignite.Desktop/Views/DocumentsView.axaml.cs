using Avalonia.Controls;

namespace Webignite.Desktop.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView() => InitializeComponent();

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
