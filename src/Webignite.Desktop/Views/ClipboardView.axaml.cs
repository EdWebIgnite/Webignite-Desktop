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
        tools.CopyClip = async text =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
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
