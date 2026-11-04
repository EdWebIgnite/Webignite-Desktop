using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // Each preview carries its own palette, so it shows that look whatever the app is using now.
        PreviewGraphite.Resources.MergedDictionaries.Add(Palettes.Load(DarkPalette.Graphite));
        PreviewMidnight.Resources.MergedDictionaries.Add(Palettes.Load(DarkPalette.Midnight));
        PreviewWarm.Resources.MergedDictionaries.Add(Palettes.Load(DarkPalette.Warm));
    }

    private void ChooseBackupFolder(object? sender, RoutedEventArgs e) => ChooseBackupFolderAsync(sender, e).Forget("Choosing the backup folder", tell: true);

    private async Task ChooseBackupFolderAsync(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where should backups go?" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            settings.SetBackupFolder(path);
    }

    private void ChooseClientRoot(object? sender, RoutedEventArgs e) => ChooseClientRootAsync().Forget("Choosing the client folders", tell: true);

    private async Task ChooseClientRootAsync()
    {
        if (DataContext is not SettingsViewModel settings || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where are your client folders?" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            settings.SetClientRoot(path);
    }

    private void ChooseRestoreFile(object? sender, RoutedEventArgs e) => ChooseRestoreFileAsync(sender, e).Forget("Choosing a backup to restore", tell: true);

    private async Task ChooseRestoreFileAsync(object? sender, RoutedEventArgs e)
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
