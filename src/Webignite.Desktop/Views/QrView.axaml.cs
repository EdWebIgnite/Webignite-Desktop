using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>QR codes for a web address, Wi-Fi, a phone number or plain text, saved as PNG or SVG.</summary>
public partial class QrView : UserControl
{
    public QrView() => InitializeComponent();

    private ToolsViewModel? Tools => DataContext as ToolsViewModel;

    private void SaveQrPng(object? sender, RoutedEventArgs e) => SaveQrPngAsync(sender, e).Forget("Saving the QR code", tell: true);

    private async Task SaveQrPngAsync(object? sender, RoutedEventArgs e)
    {
        if (Tools?.QrPng() is not { } png || await PickFile("QR code.png", "PNG image", "*.png") is not { } file)
            return;
        await using (var stream = await file.OpenWriteAsync())
            await stream.WriteAsync(png);
        Tools.QrSaved(file.Name);
    }

    private void SaveQrSvg(object? sender, RoutedEventArgs e) => SaveQrSvgAsync(sender, e).Forget("Saving the QR code", tell: true);

    private async Task SaveQrSvgAsync(object? sender, RoutedEventArgs e)
    {
        if (Tools?.QrSvg() is not { } svg || await PickFile("QR code.svg", "SVG image", "*.svg") is not { } file)
            return;
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream))
            await writer.WriteAsync(svg);
        Tools.QrSaved(file.Name);
    }

    private Task<IStorageFile?> PickFile(string name, string kind, string pattern) =>
        TopLevel.GetTopLevel(this)?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the QR code",
            SuggestedFileName = name,
            FileTypeChoices = [new FilePickerFileType(kind) { Patterns = [pattern] }],
        }) ?? Task.FromResult<IStorageFile?>(null);
}
