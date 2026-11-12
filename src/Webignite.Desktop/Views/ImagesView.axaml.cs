using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

namespace Webignite.Desktop.Views;

/// <summary>Website images: choose or drop photos, and smaller copies go in a web folder beside them.</summary>
public partial class ImagesView : UserControl
{
    public ImagesView()
    {
        InitializeComponent();
        ImageDrop.AddHandler(DragDrop.DragOverEvent, ImagesOver);
        ImageDrop.AddHandler(DragDrop.DropEvent, ImagesDropped);
    }

    private ImagesToolViewModel? Tools => DataContext as ImagesToolViewModel;

    private void ChooseImages(object? sender, RoutedEventArgs e) => ChooseImagesAsync(sender, e).Forget("Making the images smaller", tell: true);

    private async Task ChooseImagesAsync(object? sender, RoutedEventArgs e)
    {
        if (Tools is not { } tools)
            return;
        var files = await Picker.Files(this, "Choose images to make smaller", ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp"], many: true, kindName: "Images");
        if (files.Count > 0)
            await tools.ShrinkImagesAsync(files);
    }

    private void ImagesOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && Tools is { ImagesBusy: false } ? DragDropEffects.Copy : DragDropEffects.None;

    private void ImagesDropped(object? sender, DragEventArgs e) => ImagesDroppedAsync(sender, e).Forget("Making the images smaller", tell: true);

    private async Task ImagesDroppedAsync(object? sender, DragEventArgs e)
    {
        if (Tools is not { ImagesBusy: false } tools || e.DataTransfer.TryGetFiles() is not { } items)
            return;
        // A dropped folder brings the images directly inside it.
        var paths = new List<string>();
        foreach (var item in items)
        {
            if (item.TryGetLocalPath() is not { } path)
                continue;
            if (Directory.Exists(path))
                paths.AddRange(Directory.EnumerateFiles(path));
            else
                paths.Add(path);
        }
        await tools.ShrinkImagesAsync(paths);
    }
}
