using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MailSetupView : UserControl
{
    public MailSetupView() => InitializeComponent();

    private void ChooseSignatureImage(object? sender, RoutedEventArgs e) => ChooseSignatureImageAsync().Forget("Choosing the signature picture", tell: true);

    private async Task ChooseSignatureImageAsync()
    {
        if (DataContext is not MailSetupViewModel setup || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
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
