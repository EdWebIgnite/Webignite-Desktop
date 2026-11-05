using Avalonia;
using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Clipboard history in its tool window. It follows the history only while it's showing.</summary>
public partial class ClipboardView : UserControl
{
    private ToolsViewModel? watching;

    public ClipboardView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not ToolsViewModel tools)
            return;
        watching = tools;
        tools.CopyClip = async clip =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
                return;
            if (clip.Picture is { } png)
            {
                using var stream = new MemoryStream(png);
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                var item = new Avalonia.Input.DataTransferItem();
                item.SetBitmap(bitmap);
                var data = new Avalonia.Input.DataTransfer();
                data.Add(item);
                await clipboard.SetDataAsync(data);
            }
            else
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, clip.Text ?? "");
            }
        };
        tools.WatchClipboard();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        watching?.StopClipboard();
        if (watching is not null)
            watching.CopyClip = null;
        watching = null;
        base.OnDetachedFromVisualTree(e);
    }
}
