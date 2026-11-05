using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

namespace Webignite.Desktop.Views;

/// <summary>
/// The PDF tools page: opening and dropping files, dragging pages into order, Delete to take pages out, and
/// the fill-in page: clicking to place, dragging to move, and the signature pad.
/// </summary>
public partial class PdfView : FullBleedView
{
    private static readonly DataFormat<string> PageDrag = DataFormat.CreateInProcessFormat<string>("webignite-pdf-pages");

    private PointerPressedEventArgs? pressed;
    private Point pressedAt;
    private Point? stampFrom;
    private Polyline? stroke;

    public PdfView()
    {
        InitializeComponent();
        PageList.SelectionChanged += (_, _) =>
        {
            if (Pdf is { } pdf)
                pdf.Picked = [.. PageList.Selection.SelectedItems.OfType<PdfPageRow>()];
        };
        PageList.AddHandler(PointerPressedEvent, PagePressedForDrag, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageList.AddHandler(PointerMovedEvent, PageMovedForDrag, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageList.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageList.AddHandler(DragDrop.DragOverEvent, DraggedOver);
        PageList.AddHandler(DragDrop.DropEvent, Dropped);
        PageList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && Pdf is { } pdf)
            {
                pdf.RemoveCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private PdfViewModel? Pdf => DataContext as PdfViewModel;

    // Opening and saving

    private void OpenFiles(object? sender, RoutedEventArgs e) => OpenFilesAsync().Forget("Opening PDFs", tell: true);

    private async Task OpenFilesAsync()
    {
        if (Pdf is not { } pdf)
            return;
        var files = await Picker.Files(this, "Open PDFs or pictures", ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"], many: true, kindName: "PDFs and pictures");
        if (files.Count > 0)
            await pdf.Open(files);
    }

    private void SaveAs(object? sender, RoutedEventArgs e) => SaveAsAsync().Forget("Saving the PDF", tell: true);

    private async Task SaveAsAsync()
    {
        if (Pdf is not { Main: { } main } pdf)
            return;
        var start = main.IsTemporary ? null : System.IO.Path.GetDirectoryName(main.Path);
        if (await Picker.Save(this, "Save the PDF", System.IO.Path.GetFileNameWithoutExtension(main.Name) + ".pdf", ["*.pdf"], "PDF", start) is { } path)
            await pdf.SaveAs(path);
    }

    private void SplitEach(object? sender, RoutedEventArgs e) => Pdf?.Split(1).Forget("Splitting the PDF", tell: true);
    private void SplitTwos(object? sender, RoutedEventArgs e) => Pdf?.Split(2).Forget("Splitting the PDF", tell: true);
    private void SplitFives(object? sender, RoutedEventArgs e) => Pdf?.Split(5).Forget("Splitting the PDF", tell: true);

    // Dragging pages into order, and files in from elsewhere

    private void PagePressedForDrag(object? sender, PointerPressedEventArgs e)
    {
        pressed = null;
        if (!e.GetCurrentPoint(PageList).Properties.IsLeftButtonPressed || e.KeyModifiers != KeyModifiers.None
            || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
            return;
        pressed = e;
        pressedAt = e.GetPosition(PageList);
    }

    private void PageMovedForDrag(object? sender, PointerEventArgs e) => DragPages(e).Forget("Moving pages");

    private async Task DragPages(PointerEventArgs e)
    {
        if (pressed is not { } start || !e.GetCurrentPoint(PageList).Properties.IsLeftButtonPressed)
            return;
        var moved = e.GetPosition(PageList) - pressedAt;
        if (Math.Abs(moved.X) < 8 && Math.Abs(moved.Y) < 8)
            return;
        pressed = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(PageDrag, "pages"));
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
    }

    private void DraggedOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(PageDrag) ? DragDropEffects.Move : e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Dropped(object? sender, DragEventArgs e)
    {
        if (Pdf is not { } pdf)
            return;
        e.Handled = true;
        if (e.DataTransfer.Contains(PageDrag))
        {
            // Dropped on a page: before it, or after it when on its right half. Anywhere else: at the end.
            var target = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            var before = pdf.Pages.Count;
            if (target?.DataContext is PdfPageRow row)
            {
                before = pdf.Pages.IndexOf(row);
                if (e.GetPosition(target).X > target.Bounds.Width / 2)
                    before++;
            }
            pdf.MovePicked(before);
            return;
        }
        if (e.DataTransfer.TryGetFiles() is { } files)
            pdf.Open([.. files.Select(f => f.TryGetLocalPath()).OfType<string>()]).Forget("Opening PDFs", tell: true);
    }

    // Filling in: a click on the page puts down text, the date or the signature.

    private void PagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Pdf?.Filling is not { } filling || e.Source != FillPage && e.Source is not Image)
            return;
        var at = e.GetPosition(FillPage);
        var row = filling.PlaceAt(at.X / FillPage.Bounds.Width, at.Y / FillPage.Bounds.Height);
        e.Handled = row is not null;
    }

    private void StampTextShown(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox { DataContext: PdfStampRow { IsEditing: true } row } box)
        {
            row.IsEditing = false;
            Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Input);
        }
    }

    // Dragging a stamp by its edge (a signature anywhere on it).
    private void StampPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBox or Button || (e.Source as Visual)?.FindAncestorOfType<TextBox>() is not null || (e.Source as Visual)?.FindAncestorOfType<Button>() is not null)
            return;
        stampFrom = e.GetPosition(FillPage);
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void StampMoved(object? sender, PointerEventArgs e)
    {
        if (stampFrom is not { } from || (sender as Control)?.DataContext is not PdfStampRow row)
            return;
        var now = e.GetPosition(FillPage);
        row.MoveBy(now.X - from.X, now.Y - from.Y);
        stampFrom = now;
    }

    private void StampReleased(object? sender, PointerReleasedEventArgs e)
    {
        stampFrom = null;
        e.Pointer.Capture(null);
    }

    // The signature pad: strokes drawn with the mouse or a pen, kept as a PNG with nothing behind them.

    private void PadPressed(object? sender, PointerPressedEventArgs e)
    {
        stroke = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(20, 30, 70)), StrokeThickness = 2.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        stroke.Points.Add(e.GetPosition(Pad));
        Pad.Children.Add(stroke);
        e.Pointer.Capture(Pad);
    }

    private void PadMoved(object? sender, PointerEventArgs e) => stroke?.Points.Add(e.GetPosition(Pad));

    private void PadReleased(object? sender, PointerReleasedEventArgs e)
    {
        stroke = null;
        e.Pointer.Capture(null);
    }

    private void PadClear(object? sender, RoutedEventArgs e) => Pad.Children.Clear();

    private void PadCancel(object? sender, RoutedEventArgs e)
    {
        Pad.Children.Clear();
        if (Pdf?.Filling is { } filling)
            filling.Drawing = false;
    }

    private void PadKeep(object? sender, RoutedEventArgs e)
    {
        if (Pdf?.Filling is not { } filling || Pad.Children.Count == 0)
            return;
        filling.KeepSignature(SignaturePng(Pad));
        Pad.Children.Clear();
    }

    /// <summary>The strokes on the pad, cropped to what was drawn, as a PNG with a see-through background.</summary>
    public static byte[] SignaturePng(Canvas pad)
    {
        var points = pad.Children.OfType<Polyline>().SelectMany(p => p.Points).ToList();
        var left = Math.Max(0, points.Min(p => p.X) - 6);
        var top = Math.Max(0, points.Min(p => p.Y) - 6);
        var right = Math.Min(pad.Bounds.Width > 0 ? pad.Bounds.Width : pad.Width, points.Max(p => p.X) + 6);
        var bottom = Math.Min(pad.Bounds.Height > 0 ? pad.Bounds.Height : pad.Height, points.Max(p => p.Y) + 6);
        const double scale = 3; // sharp when printed
        var size = new PixelSize((int)Math.Ceiling((right - left) * scale), (int)Math.Ceiling((bottom - top) * scale));
        using var target = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        using (var context = target.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateTranslation(-left, -top)))
        {
            foreach (var line in pad.Children.OfType<Polyline>())
            {
                var pen = new Pen(line.Stroke, line.StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                for (var i = 1; i < line.Points.Count; i++)
                    context.DrawLine(pen, line.Points[i - 1], line.Points[i]);
                if (line.Points.Count == 1)
                    context.DrawEllipse(line.Stroke, null, line.Points[0], line.StrokeThickness / 2, line.StrokeThickness / 2);
            }
        }
        using var stream = new MemoryStream();
        target.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }
}
