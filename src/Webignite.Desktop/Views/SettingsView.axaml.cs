using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void ChooseBackupFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where should backups go?" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            settings.SetBackupFolder(path);
    }

    private async void ChooseRestoreFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var start = await storage.TryGetFolderFromPathAsync(settings.BackupFolder);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a Webignite backup",
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType("Webignite backups") { Patterns = ["*.wbak"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            settings.ChooseRestoreFile(path);
    }
}
