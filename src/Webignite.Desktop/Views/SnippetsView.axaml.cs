using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Text snippets in their tool window. Opened by the shortcut from another program, picking one closes the
/// window and pastes it where the person was typing; opened from the sidebar, it is copied.
/// </summary>
public partial class SnippetsView : UserControl
{
    private ToolsViewModel? tools;

    public SnippetsView() => InitializeComponent();

    /// <summary>The window of the other program to paste into, when opened by the shortcut. Windows only.</summary>
    public IntPtr PasteInto { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not ToolsViewModel model)
            return;
        tools = model;
        model.SendSnippet = SendAsync;
        model.ReadClipboard = async () => TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
            ? await clipboard.TryGetTextAsync() : null;
        model.LoadSnippets();
        SearchBox.Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (tools is not null)
        {
            tools.SendSnippet = null;
            tools.ReadClipboard = null;
        }
        tools = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async Task<string> SendAsync(string text)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is not { } clipboard)
            return "";
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        if (!OperatingSystem.IsWindows() || PasteInto == IntPtr.Zero)
            return "Copied. Paste it where you need it.";
        // Out of the way first, so the other program is in front when Ctrl+V is pressed.
        var target = PasteInto;
        (top as Window)?.Close();
        if (!await PasteElsewhere.PasteAsync(target))
            App.Services?.ShowNote?.Invoke("Snippet copied", "It couldn't be pasted where you were. It is on the clipboard: press Ctrl+V.", null);
        return "";
    }
}
