using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MarkupView : UserControl
{
    private DispatcherTimer? waitingForSnip;

    // The window the Ctrl+V and Ctrl+Z handler went on. Asking for the window again when the page is taken
    // off screen finds nothing, which left the handler, and the whole page, held by the window for good.
    private TopLevel? keysOn;
    private DateTime snipStarted;

    public MarkupView()
    {
        InitializeComponent();
        SnipButton.IsVisible = OperatingSystem.IsWindows();
        Canvas.MarkDrawn = mark => Markup?.Add(mark);
        DragDrop.SetAllowDrop(Canvas, true);
        Canvas.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        Canvas.AddHandler(DragDrop.DropEvent, Dropped);
    }

    private MarkupViewModel? Markup => DataContext as MarkupViewModel;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Markup?.LoadClients();
        keysOn = TopLevel.GetTopLevel(this);
        keysOn?.AddHandler(KeyDownEvent, Keys, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StopWaiting();
        keysOn?.RemoveHandler(KeyDownEvent, Keys);
        keysOn = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Ctrl+V pastes a screenshot, unless the cursor is in a text box. Ctrl+Z undoes the last mark.
    private void Keys(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;
        if (e.Key == Key.V)
        {
            PasteImage(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Markup?.UndoCommand.CanExecute(null) == true)
        {
            Markup.UndoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PasteImage(object? sender, RoutedEventArgs e) => PasteImageAsync(sender, e).Forget("Pasting the picture", tell: true);

    private async Task PasteImageAsync(object? sender, RoutedEventArgs e)
    {
        if (Markup is not { } markup || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
            markup.Show(bitmap);
        else
            markup.Say("There's no picture to paste. Press Win+Shift+S, drag over the site, then Paste.", problem: true);
    }

    // Windows: opens the snipping bar, then watches the clipboard for the new picture for a minute.
    private void TakeScreenshot(object? sender, RoutedEventArgs e) => TakeScreenshotAsync(sender, e).Forget("Taking the screenshot", tell: true);

    private async Task TakeScreenshotAsync(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || Markup is not { } markup || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        await clipboard.ClearAsync();
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            markup.Say("Couldn't open the snipping bar. Press Win+Shift+S, then Paste here.", problem: true);
            return;
        }
        markup.Say("Drag over the part of the screen you want. It appears here straight after.");
        snipStarted = DateTime.UtcNow;
        StopWaiting();
        waitingForSnip = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, async (_, _) =>
        {
            if (DateTime.UtcNow - snipStarted > TimeSpan.FromMinutes(1))
            {
                StopWaiting();
                markup.Say("No screenshot came through. Try again, or press Win+Shift+S and then Paste.");
                return;
            }
            if (await clipboard.TryGetBitmapAsync() is { } bitmap)
            {
                StopWaiting();
                markup.Show(bitmap);
                if (TopLevel.GetTopLevel(this) is Window window)
                {
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Activate();
                }
            }
        });
        waitingForSnip.Start();
    }

    private void StopWaiting()
    {
        waitingForSnip?.Stop();
        waitingForSnip = null;
    }

    private void OpenImage(object? sender, RoutedEventArgs e) => OpenImageAsync(sender, e).Forget("Opening the picture", tell: true);

    private async Task OpenImageAsync(object? sender, RoutedEventArgs e)
    {
        if (Markup is not { } markup || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a screenshot",
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            Load(path);
    }

    private void Dropped(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path)
            Load(path);
    }

    private void Load(string path)
    {
        try
        {
            Markup?.Show(new Bitmap(path));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            Markup?.Say("Couldn't open that as a picture.", problem: true);
        }
    }

    private void SavePng(object? sender, RoutedEventArgs e) => SavePngAsync(sender, e).Forget("Saving the picture", tell: true);

    private async Task SavePngAsync(object? sender, RoutedEventArgs e)
    {
        if (Markup is not { } markup || markup.Png() is not { } png || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the marked-up picture",
            SuggestedFileName = markup.FileName,
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        if (file is null)
            return;
        await using (var stream = await file.OpenWriteAsync())
            await stream.WriteAsync(png);
        markup.Say($"Saved {file.Name}.");
    }
}
