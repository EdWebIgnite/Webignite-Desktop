using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TheArtOfDev.HtmlRenderer.Avalonia;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MailView : UserControl
{
    public MailView()
    {
        InitializeComponent();
        // Links in mail open in the normal browser, never inside the app.
        Body.LinkClicked += (_, e) =>
        {
            e.Event.Handled = true;
            if (Uri.TryCreate(e.Event.Link, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" or "mailto")
                _ = AppServices.OpenInBrowser(url);
        };
    }

    private async void ChooseSignatureImage(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MailSetupViewModel setup || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a signature picture",
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            setup.SetSignatureImage(path);
    }
}
