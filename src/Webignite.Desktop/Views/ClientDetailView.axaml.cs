using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

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
        if (DataContext is not ClientDetailViewModel client)
            return;
        client.AddFiles(await Picker.Files(this, "Link files to " + client.Title, many: true, start: client.HasFolder ? client.FolderPath : null));
    }

    private void PickFolder(object? sender, RoutedEventArgs e) => PickFolderAsync().Forget("Picking the client's folder", tell: true);

    private async Task PickFolderAsync()
    {
        if (DataContext is not ClientDetailViewModel client)
            return;
        if (await Picker.Folder(this, client.Title + "'s folder", client.HasFolder ? client.FolderPath : null) is { } path)
            client.UseFolder(path);
    }
}
