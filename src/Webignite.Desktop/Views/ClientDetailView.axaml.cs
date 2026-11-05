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

    // Side by side when there's room; in a narrow pane the rows go under the details, full width.
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var narrow = e.NewSize.Width < 860;
        if (Grid.GetRow(SectionsCard) == (narrow ? 1 : 0))
            return;
        Grid.SetRow(SectionsCard, narrow ? 1 : 0);
        Grid.SetColumn(SectionsCard, narrow ? 0 : 2);
        Grid.SetColumnSpan(SectionsCard, narrow ? 3 : 1);
        SectionsCard.Margin = narrow ? new Avalonia.Thickness(0, 18, 0, 0) : default;
        Columns.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1.25, GridUnitType.Star);
        Columns.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(24);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ClientDetailViewModel client)
            client.Copy = async (text, secret) =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
                    return;
                if (secret)
                    await SecretClipboard.CopyAsync(clipboard, text);
                else
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            };
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
