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

    private async void LinkFiles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientDetailViewModel client || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Link files to " + client.Title, AllowMultiple = true });
        client.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
}
