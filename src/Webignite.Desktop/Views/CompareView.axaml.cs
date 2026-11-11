using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Compare: two files side by side, or two folders file by file. Only reads.</summary>
public partial class CompareView : UserControl
{
    public CompareView() => InitializeComponent();

    private ToolsViewModel? Tools => DataContext as ToolsViewModel;

    private void BrowseLeftFile(object? sender, RoutedEventArgs e) => PickAsync(folder: false, left: true).Forget("Picking a file to compare", tell: true);

    private void BrowseLeftFolder(object? sender, RoutedEventArgs e) => PickAsync(folder: true, left: true).Forget("Picking a folder to compare", tell: true);

    private void BrowseRightFile(object? sender, RoutedEventArgs e) => PickAsync(folder: false, left: false).Forget("Picking a file to compare", tell: true);

    private void BrowseRightFolder(object? sender, RoutedEventArgs e) => PickAsync(folder: true, left: false).Forget("Picking a folder to compare", tell: true);

    private async Task PickAsync(bool folder, bool left)
    {
        var picked = folder
            ? await Picker.Folder(this, left ? "The folder on the left" : "The folder on the right")
            : (await Picker.Files(this, left ? "The file on the left" : "The file on the right")).FirstOrDefault();
        if (picked is null || Tools is not { } tools)
            return;
        if (left)
            tools.CompareLeft = picked;
        else
            tools.CompareRight = picked;
    }
}
