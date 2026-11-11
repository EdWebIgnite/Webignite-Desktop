using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (DataContext is ChatViewModel chat && e.DataTransfer.TryGetFiles() is { } files)
                chat.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        });
        // A pasted picture (a screenshot) goes with the message. Enter is caught on its way in: the box, taking new
        // lines, would otherwise keep Enter for itself and the message would never go.
        DraftBox.AddHandler(KeyDownEvent, PasteKeyDown, RoutingStrategies.Tunnel);
        DraftBox.AddHandler(KeyDownEvent, DraftKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the newest message at the bottom.</summary>
    public void ScrollToEnd() => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);

    public void FocusDraft() => DraftBox.Focus();

    private void DraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && DataContext is ChatViewModel chat)
        {
            e.Handled = true;
            chat.SendCommand.Execute(null);
            ScrollToEnd();
        }
    }

    private void PasteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            PastePictureAsync(e).Forget("Pasting a picture into the chat", tell: true);
    }

    private async Task PastePictureAsync(KeyEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || DataContext is not ChatViewModel chat)
            return;
        var formats = await clipboard.GetDataFormatsAsync();
        if (!formats.Contains(DataFormat.Bitmap) || formats.Contains(DataFormat.Text))
            return;
        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            using (bitmap)
            using (var png = new MemoryStream())
            {
                bitmap.Save(png, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                chat.AddPicture(png.ToArray());
            }
            e.Handled = true;
        }
    }

    private void Attach(object? sender, RoutedEventArgs e) => AttachAsync().Forget("Attaching files to a chat message", tell: true);

    private async Task AttachAsync()
    {
        if (DataContext is ChatViewModel chat)
            chat.AddFiles(await Picker.Files(this, "Attach files", many: true));
    }
}
