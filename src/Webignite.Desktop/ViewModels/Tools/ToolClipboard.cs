using Avalonia.Controls;

namespace Webignite.Desktop.ViewModels;

/// <summary>Copying an answer out of a tool, the same way for every tool.</summary>
public static class ToolClipboard
{
    /// <summary>Puts the text on the clipboard of the window the tool is in. False when there was nothing to copy or nowhere to put it.</summary>
    public static async Task<bool> CopyAsync(TopLevel? top, string? text)
    {
        if (string.IsNullOrEmpty(text) || top?.Clipboard is not { } clipboard)
            return false;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        return true;
    }
}
