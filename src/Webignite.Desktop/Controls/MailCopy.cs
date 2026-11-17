using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TheArtOfDev.HtmlRenderer.Avalonia;
using Webignite.Core.Writing;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Copying out of a mail being read. The part that draws a mail has a copy of its own, which often left nothing
/// on Windows' clipboard and, when it did, left text full of web codes ("Hi&amp;nbsp;Ronald"). So its own menu is
/// switched off and Ctrl+C and the right-click menu here do the copying: the selected words go to the clipboard
/// as plain text, as everywhere else in the app, and the same selection with its bold, links and lists is kept
/// for pasting into a mail or a note in Webignite.
/// </summary>
public static class MailCopy
{
    public static void Attach(HtmlPanel panel)
    {
        panel.IsContextMenuEnabled = false;
        panel.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            var copy = (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control) || (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.Control);
            if (!copy)
                return;
            e.Handled = true;
            CopyAsync(panel).Forget("Copying from a mail", tell: true);
        }, RoutingStrategies.Tunnel);
        panel.ContextRequested += (_, e) =>
        {
            var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control), IsEnabled = !string.IsNullOrEmpty(panel.SelectedText) };
            copy.Click += (_, _) => CopyAsync(panel).Forget("Copying from a mail", tell: true);
            new ContextMenu { ItemsSource = new[] { copy } }.Open(panel);
            e.Handled = true;
        };
    }

    private static async Task CopyAsync(HtmlPanel panel)
    {
        var selected = panel.SelectedText;
        if (string.IsNullOrEmpty(selected) || TopLevel.GetTopLevel(panel)?.Clipboard is not { } clipboard)
            return;
        var text = RichHtml.Uncoded(selected);
        if (!await ClipboardText.SetTextAsync(clipboard, text))
        {
            App.Services?.ShowNote?.Invoke("Not copied", "Windows' clipboard is being used by another program. Try again in a moment.", null);
            return;
        }
        // With its formatting, for a paste inside the app.
        if (panel.SelectedHtml is { Length: > 0 } html)
            RichTextEditor.Remember(text, RichHtml.Read(html));
    }
}

/// <summary>Putting text on Windows' clipboard so that it gets there.</summary>
public static class ClipboardText
{
    /// <summary>
    /// Sets the clipboard's text. Another program can hold the clipboard for a moment (a clipboard manager, remote
    /// desktop), so it is tried a few times. False when it still couldn't be set.
    /// </summary>
    public static async Task<bool> SetTextAsync(IClipboard clipboard, string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await clipboard.SetTextAsync(text);
                return true;
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
            {
                await Task.Delay(60);
            }
        }
        return false;
    }
}
