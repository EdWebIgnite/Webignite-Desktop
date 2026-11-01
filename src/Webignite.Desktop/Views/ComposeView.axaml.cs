using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ComposeView : UserControl
{
    public ComposeView() => InitializeComponent();

    private async void OnAttach(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ComposeViewModel compose || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Attach files", AllowMultiple = true });
        compose.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
}
