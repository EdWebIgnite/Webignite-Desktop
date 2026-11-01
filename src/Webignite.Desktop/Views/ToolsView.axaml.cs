using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ToolsView : UserControl
{
    private DispatcherTimer? picker;
    private string? colourBeforePicking;
    private DateTime pickingSince;

    public ToolsView()
    {
        InitializeComponent();
        ImageDrop.AddHandler(DragDrop.DragOverEvent, ImagesOver);
        ImageDrop.AddHandler(DragDrop.DropEvent, ImagesDropped);
    }

    private async void ChooseImages(object? sender, RoutedEventArgs e)
    {
        if (Tools is not { } tools || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose images to make smaller",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp"] }],
        });
        await tools.ShrinkImagesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private void ImagesOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && Tools is { ImagesBusy: false } ? DragDropEffects.Copy : DragDropEffects.None;

    private async void ImagesDropped(object? sender, DragEventArgs e)
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

    private ToolsViewModel? Tools => DataContext as ToolsViewModel;

    private async void SaveQrPng(object? sender, RoutedEventArgs e)
    {
        if (Tools?.QrPng() is not { } png || await PickFile("QR code.png", "PNG image", "*.png") is not { } file)
            return;
        await using (var stream = await file.OpenWriteAsync())
            await stream.WriteAsync(png);
        Tools.QrSaved(file.Name);
    }

    private async void SaveQrSvg(object? sender, RoutedEventArgs e)
    {
        if (Tools?.QrSvg() is not { } svg || await PickFile("QR code.svg", "SVG image", "*.svg") is not { } file)
            return;
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream))
            await writer.WriteAsync(svg);
        Tools.QrSaved(file.Name);
    }

    private Task<IStorageFile?> PickFile(string name, string kind, string pattern) =>
        TopLevel.GetTopLevel(this)?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the QR code",
            SuggestedFileName = name,
            FileTypeChoices = [new FilePickerFileType(kind) { Patterns = [pattern] }],
        }) ?? Task.FromResult<IStorageFile?>(null);

    // Windows: the colour under the pointer is shown live until Enter keeps it or Esc puts the old one back.
    private void PickFromScreen(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || Tools is not { } tools || picker is not null)
            return;
        colourBeforePicking = tools.ColourInput;
        pickingSince = DateTime.UtcNow;
        tools.Picking = true;
        if (TopLevel.GetTopLevel(this) is { } top)
            top.AddHandler(KeyDownEvent, PickingKeys, RoutingStrategies.Tunnel);
        picker = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Input, (_, _) => Sample());
        picker.Start();
    }

    private void Sample()
    {
        // Give up after a minute so the timer never runs forever.
        if (DateTime.UtcNow - pickingSince > TimeSpan.FromMinutes(1))
        {
            StopPicking(keep: true);
            return;
        }
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var point))
            return;
        var screen = GetDC(IntPtr.Zero);
        try
        {
            var colour = GetPixel(screen, point.X, point.Y);
            if (colour != uint.MaxValue)
                Tools?.ShowPicked((byte)(colour & 0xFF), (byte)(colour >> 8 & 0xFF), (byte)(colour >> 16 & 0xFF));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void PickingKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            StopPicking(keep: e.Key == Key.Enter);
            e.Handled = true;
        }
    }

    private void StopPicking(bool keep)
    {
        picker?.Stop();
        picker = null;
        TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, PickingKeys);
        if (Tools is { } tools)
        {
            tools.Picking = false;
            if (!keep && colourBeforePicking is not null)
                tools.ColourInput = colourBeforePicking;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StopPicking(keep: true);
        base.OnDetachedFromVisualTree(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
