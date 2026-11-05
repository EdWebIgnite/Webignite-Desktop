using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

namespace Webignite.Desktop.Views;

/// <summary>QR codes for a web address, Wi-Fi, a phone number or plain text, saved as PNG or SVG.</summary>
public partial class QrView : UserControl
{
    public QrView() => InitializeComponent();

    private ToolsViewModel? Tools => DataContext as ToolsViewModel;

    private void SaveQrPng(object? sender, RoutedEventArgs e) => SaveQrPngAsync(sender, e).Forget("Saving the QR code", tell: true);

    private async Task SaveQrPngAsync(object? sender, RoutedEventArgs e)
    {
        if (Tools?.QrPng() is not { } png || await Picker.Save(this, "Save the QR code", "QR code.png", ["*.png"], "PNG image") is not { } path)
            return;
        await File.WriteAllBytesAsync(path, png);
        Tools.QrSaved(System.IO.Path.GetFileName(path));
    }

    private void SaveQrSvg(object? sender, RoutedEventArgs e) => SaveQrSvgAsync(sender, e).Forget("Saving the QR code", tell: true);

    private async Task SaveQrSvgAsync(object? sender, RoutedEventArgs e)
    {
        if (Tools?.QrSvg() is not { } svg || await Picker.Save(this, "Save the QR code", "QR code.svg", ["*.svg"], "SVG image") is not { } path)
            return;
        await File.WriteAllTextAsync(path, svg);
        Tools.QrSaved(System.IO.Path.GetFileName(path));
    }

}
