using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ClientDetailView : UserControl
{
    public ClientDetailView()
    {
        InitializeComponent();
        FilesDrop.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Link : DragDropEffects.None);
        FilesDrop.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (DataContext is ClientDetailViewModel client && e.DataTransfer.TryGetFiles() is { } files)
                client.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        });
    }

    private void LinkFiles(object? sender, RoutedEventArgs e) => LinkFilesAsync(sender, e).Forget("Linking files", tell: true);

    private async Task LinkFilesAsync(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientDetailViewModel client || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Link files to " + client.Title, AllowMultiple = true });
        client.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private void PickFolder(object? sender, RoutedEventArgs e) => PickFolderAsync().Forget("Picking the client's folder", tell: true);

    private async Task PickFolderAsync()
    {
        if (DataContext is not ClientDetailViewModel client || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = client.Title + "'s folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            client.UseFolder(path);
    }
}
