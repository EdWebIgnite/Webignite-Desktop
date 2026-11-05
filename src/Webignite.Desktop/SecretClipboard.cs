using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Webignite.Core.Clients;

namespace Webignite.Desktop;

/// <summary>
/// Copies a password so it can be pasted once and is then gone: Windows is asked to leave it out of clipboard
/// history (Win+V) and cloud sync, and Webignite clears it after <see cref="SiteLogins.ClipboardLife"/> unless
/// something else was copied meanwhile.
/// </summary>
public static class SecretClipboard
{
    // The formats Windows' clipboard history and cloud clipboard look for. Their presence (with a DWORD 0 for
    // the last two) tells them to skip this copy.
    private static readonly DataFormat<byte[]> Exclude = DataFormat.CreateBytesPlatformFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly DataFormat<byte[]> InHistory = DataFormat.CreateBytesPlatformFormat("CanIncludeInClipboardHistory");
    private static readonly DataFormat<byte[]> ToCloud = DataFormat.CreateBytesPlatformFormat("CanUploadToCloudClipboard");

    public static async Task CopyAsync(IClipboard clipboard, string secret)
    {
        var item = new DataTransferItem();
        item.SetText(secret);
        if (OperatingSystem.IsWindows())
        {
            item.Set(Exclude, [0]);
            item.Set(InHistory, [0, 0, 0, 0]);
            item.Set(ToCloud, [0, 0, 0, 0]);
        }
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
        ClearLater(clipboard, secret).Forget("Clearing a copied password");
    }

    private static async Task ClearLater(IClipboard clipboard, string secret)
    {
        await Task.Delay(SiteLogins.ClipboardLife);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (await clipboard.TryGetTextAsync() == secret)
                await clipboard.ClearAsync();
        });
    }
}
