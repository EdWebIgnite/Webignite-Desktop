using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

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
        PreviewAsanaDark.Resources.MergedDictionaries.Add(Palettes.Load(DarkPalette.Asana));
        PreviewStandardLight.Resources.MergedDictionaries.Add(Palettes.Load(LightPalette.Standard));
        PreviewAsanaLight.Resources.MergedDictionaries.Add(Palettes.Load(LightPalette.Asana));
    }

    private void ChooseBackupFolder(object? sender, RoutedEventArgs e) => ChooseBackupFolderAsync(sender, e).Forget("Choosing the backup folder", tell: true);

    private async Task ChooseBackupFolderAsync(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings)
            return;
        if (await Picker.Folder(this, "Where should backups go?", settings.BackupFolder) is { } path)
            settings.SetBackupFolder(path);
    }

    private void FileTagNameLost(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileTagRow row)
            row.KeepCommand.Execute(null);
    }

    private void FileTagNameKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as Control)?.DataContext is FileTagRow row)
        {
            row.KeepCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void NewFileTagKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SettingsViewModel settings && settings.AddFileTagCommand.CanExecute(null))
        {
            settings.AddFileTagCommand.Execute(null);
            e.Handled = true;
        }
    }

    // The shortcut boxes: whatever keys are pressed in one become its shortcut. Each box names its shortcut in its Tag.
    private void ShortcutKeys(object? sender, KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel settings && (sender as Control)?.Tag is Webignite.Core.Tools.GlobalShortcut which && Pressed(e) is { } keys)
            settings.SetShortcut(which, keys);
    }

    private void QuickNotebookPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings && (sender as ComboBox)?.SelectedItem is Webignite.Core.Notes.Notebook notebook)
            settings.PickQuickNotebook(notebook);
    }

    private static string? Pressed(KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
            return null;
        e.Handled = true;
        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Win");
        parts.Add(e.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(e.Key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)(e.Key - Key.NumPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Key.Snapshot => "PrintScreen",
            Key.Scroll => "ScrollLock",
            _ => e.Key.ToString(),
        });
        return string.Join('+', parts);
    }

    private void ChooseScreenshotFolder(object? sender, RoutedEventArgs e) => ChooseScreenshotFolderAsync().Forget("Choosing the screenshot folder", tell: true);

    private async Task ChooseScreenshotFolderAsync()
    {
        if (DataContext is not SettingsViewModel settings)
            return;
        if (await Picker.Folder(this, "Where should screenshots go?", settings.ScreenshotFolder) is { } path)
            settings.SetScreenshotFolder(path);
    }

    private void ChooseClientRoot(object? sender, RoutedEventArgs e) => ChooseClientRootAsync().Forget("Choosing the client folders", tell: true);

    private async Task ChooseClientRootAsync()
    {
        if (DataContext is not SettingsViewModel settings)
            return;
        if (await Picker.Folder(this, "Where are your client folders?") is { } path)
            settings.SetClientRoot(path);
    }

    private void ChooseBrandLogo(object? sender, RoutedEventArgs e) => ChooseBrandLogoAsync().Forget("Choosing the logo for mails", tell: true);

    private async Task ChooseBrandLogoAsync()
    {
        if (DataContext is not SettingsViewModel settings)
            return;
        if (await Picker.Files(this, "Pick your logo (PNG or JPEG)", ["*.png", "*.jpg", "*.jpeg"], kindName: "Pictures") is [var path, ..])
            settings.SetBrandLogo(path);
    }

    private void ChooseRestoreFile(object? sender, RoutedEventArgs e) => ChooseRestoreFileAsync(sender, e).Forget("Choosing a backup to restore", tell: true);

    private async Task ChooseRestoreFileAsync(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings)
            return;
        if ((await Picker.Files(this, "Choose a Webignite backup", ["*.wbak"], kindName: "Webignite backups", start: settings.BackupFolder)).FirstOrDefault() is { } path)
            settings.ChooseRestoreFile(path);
    }
}
