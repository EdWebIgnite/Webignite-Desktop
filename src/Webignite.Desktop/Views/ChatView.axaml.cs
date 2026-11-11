using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Webignite.Core.Chat;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ChatView : UserControl
{
    private Point pressedAt;
    private PointerPressedEventArgs? pressedArgs;
    private Control? pressedFile;

    public ChatView()
    {
        InitializeComponent();
        Looks.Attach(this, "Chat");
        // Files dropped anywhere on the window go with the message, as WhatsApp. The box takes drops itself, so the
        // window listens for those it has already seen too.
        AddHandler(DragDrop.DragEnterEvent, DragEntered, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, DraggedOver, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropHint.IsVisible = false, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, Dropped, handledEventsToo: true);
        // Every paste goes through here, Ctrl+V and the box's right-click Paste alike: files copied in Explorer,
        // a screenshot or a picture copied from a web page go with the message; words go in the box.
        DraftBox.AddHandler(TextBox.PastingFromClipboardEvent, Pasting);
        // Enter is caught on its way in: the box, taking new lines, would otherwise keep Enter for itself.
        DraftBox.AddHandler(KeyDownEvent, DraftKeyDown, RoutingStrategies.Tunnel);
        // A file in a message can be dragged out; caught on the way in, as the button keeps the press for its click.
        Scroller.AddHandler(PointerPressedEvent, FilePressed, RoutingStrategies.Tunnel);
        Scroller.AddHandler(PointerMovedEvent, FileMoved, RoutingStrategies.Tunnel);
        Scroller.AddHandler(PointerReleasedEvent, FileReleased, RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the newest message at the bottom.</summary>
    public void ScrollToEnd() => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);

    public void FocusDraft() => DraftBox.Focus();

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);

    private void DragEntered(object? sender, DragEventArgs e) => DropHint.IsVisible = HasFiles(e) && !dragging;

    private void DraggedOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasFiles(e) && !dragging ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Dropped(object? sender, DragEventArgs e)
    {
        DropHint.IsVisible = false;
        if (dragging || DataContext is not ChatViewModel chat || e.DataTransfer.TryGetFiles() is not { } files)
            return;
        e.Handled = true;
        chat.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        DraftBox.Focus();
    }

    private void DraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && DataContext is ChatViewModel chat)
        {
            e.Handled = true;
            chat.SendCommand.Execute(null);
            ScrollToEnd();
        }
    }

    private void Pasting(object? sender, RoutedEventArgs e)
    {
        e.Handled = true; // the box would paste only words; this decides once the clipboard has been read
        PasteAsync().Forget("Pasting into the chat", tell: true);
    }

    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || DataContext is not ChatViewModel chat)
            return;
        var formats = await clipboard.GetDataFormatsAsync();
        var text = formats.Contains(DataFormat.Text) ? await clipboard.TryGetTextAsync() : null;
        switch (ChatRules.PasteAs(formats.Contains(DataFormat.File), formats.Contains(DataFormat.Bitmap), text))
        {
            case ChatPaste.Files:
                if (await clipboard.TryGetFilesAsync() is { } items)
                    chat.AddFiles(items.Select(i => i.TryGetLocalPath()).OfType<string>());
                break;
            case ChatPaste.Picture:
                if (await clipboard.TryGetBitmapAsync() is { } bitmap)
                {
                    using (bitmap)
                    using (var png = new MemoryStream())
                    {
                        bitmap.Save(png, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                        chat.AddPicture(png.ToArray());
                    }
                }
                break;
            case ChatPaste.Text:
                DraftBox.SelectedText = text!;
                break;
        }
    }

    private void Attach(object? sender, RoutedEventArgs e) => AttachAsync().Forget("Attaching files to a chat message", tell: true);

    private async Task AttachAsync()
    {
        if (DataContext is ChatViewModel chat)
            chat.AddFiles(await Picker.Files(this, "Attach files", many: true));
    }

    // A file in a message: right-click to open, copy or save it.
    private void FileMenu(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: ChatFileRow file } control)
            return;
        e.Handled = true;
        var menu = new ContextMenu();
        menu.Items.Add(Item("Open", () => file.OpenCommand.Execute(null)));
        menu.Items.Add(Item(file.IsPictureFile ? "Copy picture" : "Copy", () => CopyAsync(file).Forget("Copying a file from the chat", tell: true)));
        menu.Items.Add(Item("Save a copy…", () => SaveAsync(file).Forget("Saving a file from the chat", tell: true)));
        control.ContextMenu = menu;
        menu.Open(control);
    }

    private static MenuItem Item(string header, Action act)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => act();
        return item;
    }

    // A picture goes on the clipboard as a picture and as a file, so it pastes into Word, mail or WhatsApp as a
    // picture and into Explorer as a file; anything else as a file.
    private async Task CopyAsync(ChatFileRow file)
    {
        if (file.LocalCopy() is not { } path || TopLevel.GetTopLevel(this) is not { Clipboard: { } clipboard, StorageProvider: { } storage })
            return;
        var data = new DataTransfer();
        var item = new DataTransferItem();
        if (await storage.TryGetFileFromPathAsync(path) is { } stored)
            item.Set(DataFormat.File, stored);
        if (file.IsPictureFile)
        {
            try
            {
                item.SetBitmap(new Avalonia.Media.Imaging.Bitmap(path));
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                Webignite.Data.AppLog.Write("Copying a picture from the chat", e);
            }
        }
        data.Add(item);
        await clipboard.SetDataAsync(data);
        (DataContext as ChatViewModel)?.Told($"Copied {file.Name}.", problem: false);
    }

    private async Task SaveAsync(ChatFileRow file)
    {
        if (await Picker.Save(this, "Save a copy", file.Name) is { } target)
            file.SaveAs(target);
    }

    // Dragging a file out of a message to Explorer, the desktop, a mail or another program.
    private bool dragging;

    private static Button? FileAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: ChatFileRow } button && button.Classes.Contains("chatfile")
            ? button : null;

    private void FilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (FileAt(e.Source) is not { } control || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;
        pressedFile = control;
        pressedArgs = e;
        pressedAt = e.GetPosition(this);
    }

    private void FileReleased(object? sender, PointerReleasedEventArgs e)
    {
        pressedFile = null;
        pressedArgs = null;
    }

    private void FileMoved(object? sender, PointerEventArgs e)
    {
        if (pressedFile is not { DataContext: ChatFileRow file } || pressedArgs is not { } start
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var moved = e.GetPosition(this) - pressedAt;
        if (Math.Abs(moved.X) < 8 && Math.Abs(moved.Y) < 8)
            return;
        pressedFile = null;
        pressedArgs = null;
        DragOutAsync(file, start).Forget("Dragging a file out of the chat");
    }

    private async Task DragOutAsync(ChatFileRow file, PointerPressedEventArgs start)
    {
        if (file.LocalCopy() is not { } path || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage
            || await storage.TryGetFileFromPathAsync(path) is not { } stored)
            return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(stored));
        dragging = true;
        try
        {
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy);
        }
        catch (InvalidOperationException problem)
        {
            Webignite.Data.AppLog.Write("Dragging a file out of the chat", problem);
        }
        finally
        {
            dragging = false;
        }
    }
}
