using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

namespace Webignite.Desktop.Views;

public partial class MailSetupView : UserControl
{
    public MailSetupView() => InitializeComponent();

    private void ChooseSignatureImage(object? sender, RoutedEventArgs e) => ChooseSignatureImageAsync().Forget("Choosing the signature picture", tell: true);

    private async Task ChooseSignatureImageAsync()
    {
        if (DataContext is not MailSetupViewModel setup)
            return;
        if ((await Picker.Files(this, "Choose a signature picture", ["*.png", "*.jpg", "*.jpeg", "*.gif"], kindName: "Pictures")).FirstOrDefault() is { } path)
            setup.SetSignatureImage(path);
    }
}
