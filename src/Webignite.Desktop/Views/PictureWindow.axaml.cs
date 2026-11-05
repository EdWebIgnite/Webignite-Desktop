using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The picture viewer's window. The wheel zooms round the pointer, dragging moves a zoomed picture, and the keys
/// do the rest: ← →, + − 0 1, R, E, Ctrl+C, Delete, F11 for full screen and Esc to close. Closing lets the picture go.
/// </summary>
public partial class PictureWindow : Window
{
    private Point? draggedFrom;
    private Vector offsetAtDrag;

    /// <summary>For the designer only.</summary>
    public PictureWindow() => InitializeComponent();

    public PictureWindow(PictureViewModel picture)
    {
        InitializeComponent();
        Picture = picture;
        DataContext = picture;
        picture.CopyPicture = async bitmap =>
        {
            if (Clipboard is not { } clipboard)
                return;
            var item = new DataTransferItem();
            item.SetBitmap(bitmap);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);
        };
        picture.CopyText = async text =>
        {
            if (Clipboard is { } clipboard)
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        };
        AddHandler(KeyDownEvent, Keys, RoutingStrategies.Tunnel);
        Scroller.AddHandler(PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
        Scroller.AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        Scroller.AddHandler(PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        Scroller.AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        Scroller.SizeChanged += (_, e) => picture.SpaceIs(e.NewSize.Width - 24, e.NewSize.Height - 24);
        Closed += (_, _) => picture.Dispose();
    }

    public PictureViewModel Picture { get; } = null!;

    private void Keys(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox && e.Key != Key.Escape)
        {
            if (e.Key == Key.Enter && Picture.Resizing)
            {
                Picture.ApplyResizeCommand.Execute(null);
                e.Handled = true;
            }
            return; // typing a size in the resize boxes
        }
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        System.Windows.Input.ICommand? command = e.Key switch
        {
            Key.Right or Key.PageDown or Key.Space when !ctrl => Picture.NextCommand,
            Key.Left or Key.PageUp or Key.Back when !ctrl => Picture.PreviousCommand,
            Key.OemPlus or Key.Add => Picture.ZoomInCommand,
            Key.OemMinus or Key.Subtract => Picture.ZoomOutCommand,
            Key.D0 or Key.NumPad0 => Picture.FitCommand,
            Key.D1 or Key.NumPad1 => Picture.ActualSizeCommand,
            Key.R when shift => Picture.TurnLeftCommand,
            Key.R => Picture.TurnRightCommand,
            Key.F when shift => Picture.FlipUpDownCommand,
            Key.F => Picture.FlipAcrossCommand,
            Key.C when !ctrl => Picture.StartCropCommand,
            Key.S when !ctrl => Picture.StartResizeCommand,
            Key.I => Picture.StartPickingCommand,
            Key.Enter when Picture.Cropping => Picture.ApplyCropCommand,
            Key.E => Picture.MarkupCommand,
            Key.C when ctrl => Picture.CopyCommand,
            Key.Delete => Picture.AskDeleteCommand,
            Key.Enter when Picture.ConfirmingDelete => Picture.DeleteCommand,
            _ => null,
        };
        if (command is not null)
        {
            if (command.CanExecute(null))
                command.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (Picture.ConfirmingDelete)
                Picture.ConfirmingDelete = false;
            else if (Picture.UsingTool)
                Picture.StopToolCommand.Execute(null);
            else if (WindowState == WindowState.FullScreen)
                WindowState = WindowState.Normal;
            else
                Close();
            e.Handled = true;
        }
    }

    // The wheel zooms, keeping the spot under the pointer where it is.
    private void Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (Picture.Picture is null)
            return;
        var before = Picture.Zoom;
        var after = e.Delta.Y > 0 ? Webignite.Core.Files.PictureViewing.In(before) : Webignite.Core.Files.PictureViewing.Out(before);
        var pointer = e.GetPosition(Scroller);
        var onPicture = (Scroller.Offset + pointer) / before;
        Picture.ZoomTo(after);
        Scroller.UpdateLayout();
        Scroller.Offset = onPicture * Picture.Zoom - pointer;
        e.Handled = true;
    }

    private Point? cropFrom;

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Scroller).Properties.IsLeftButtonPressed)
            return;
        if (Picture.Picking)
        {
            Picture.PickAt(e.GetPosition(Stage)).Forget("Picking a colour from the picture");
            e.Handled = true;
            return;
        }
        if (Picture.Cropping)
        {
            cropFrom = e.GetPosition(Stage);
            e.Pointer.Capture(Scroller);
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2)
        {
            // A double-click goes between fit and actual size.
            if (Picture.Fitting)
                Picture.ActualSizeCommand.Execute(null);
            else
                Picture.FitCommand.Execute(null);
            return;
        }
        draggedFrom = e.GetPosition(this);
        offsetAtDrag = Scroller.Offset;
        e.Pointer.Capture(Scroller);
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (cropFrom is { } start)
        {
            Picture.DragCrop(start, e.GetPosition(Stage));
            return;
        }
        if (draggedFrom is not { } from)
            return;
        var now = e.GetPosition(this);
        Scroller.Offset = offsetAtDrag - (now - from);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (cropFrom is { } start)
            Picture.DragCrop(start, e.GetPosition(Stage));
        cropFrom = null;
        draggedFrom = null;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
    }
}
