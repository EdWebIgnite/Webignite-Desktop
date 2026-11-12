using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Webignite.Core.Layout;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MyPagesView : UserControl
{
    public static readonly IValueConverter Faded = new FuncValueConverter<bool, double>(other => other ? 0.4 : 1);
    public static readonly IValueConverter Bold = new FuncValueConverter<bool, FontWeight>(today => today ? FontWeight.Bold : FontWeight.Normal);
    public static readonly IValueConverter MoveCursor = new FuncValueConverter<bool, Cursor?>(arranging => arranging ? new Cursor(StandardCursorType.SizeAll) : null);

    private WidgetViewModel? held;
    private bool sizing;
    private Point grabbed;
    private Size startSize;

    public MyPagesView()
    {
        InitializeComponent();
        Board.AddHandler(PointerMovedEvent, Dragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerReleasedEvent, Dropped, RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble);
    }

    private MyPagesViewModel? Pages => DataContext as MyPagesViewModel;

    private WidgetGrid? Grid => Board.ItemsPanelRoot as WidgetGrid;

    // Picking up a widget by its heading while arranging.
    private void HeadingPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Pages is not { IsEditing: true } pages || (sender as Control)?.DataContext is not WidgetViewModel widget
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Grid is null)
            return;
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        var card = (sender as Visual)?.FindAncestorOfType<ContentPresenter>();
        if (card is null)
            return;
        held = widget;
        sizing = false;
        grabbed = e.GetPosition(card);
        pages.BeginArranging();
        e.Pointer.Capture(Board);
        e.Handled = true;
    }

    // Taking hold of a widget's corner to size it.
    private void CornerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Pages is not { IsEditing: true } pages || (sender as Control)?.DataContext is not WidgetViewModel widget || Grid is null)
            return;
        var card = (sender as Visual)?.FindAncestorOfType<ContentPresenter>();
        if (card is null)
            return;
        held = widget;
        sizing = true;
        grabbed = e.GetPosition(Grid);
        startSize = card.Bounds.Size;
        pages.BeginArranging();
        e.Pointer.Capture(Board);
        e.Handled = true;
    }

    private void Dragged(object? sender, PointerEventArgs e)
    {
        if (held is null || Pages is not { } pages || Grid is not { } grid)
            return;
        var at = e.GetPosition(grid);
        if (sizing)
        {
            var size = new Size(Math.Max(1, startSize.Width + at.X - grabbed.X), Math.Max(1, startSize.Height + at.Y - grabbed.Y));
            var (columns, rows) = grid.CellsFor(size);
            if (columns != held.Width || rows != held.Height)
                pages.ResizeTo(held, columns, rows);
        }
        else
        {
            var (column, row) = grid.NearestCell(at - grabbed);
            if (column != held.Column || row != held.Row)
                pages.MoveTo(held, column, row);
        }
    }

    private void Dropped(object? sender, PointerReleasedEventArgs e)
    {
        if (held is null)
            return;
        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (held is null)
            return;
        held = null;
        Pages?.EndArranging();
    }

    /// <summary>The kinds of widget to add, with a project or client to pick for the ones that show one.</summary>
    private void AddOpening(object? sender, EventArgs e)
    {
        if (Pages is not { } pages || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var item = new MenuItem { Header = MyPages.Label(kind) };
            if (kind == WidgetKind.Project)
            {
                foreach (var (id, name) in pages.ProjectChoices())
                {
                    var pick = new MenuItem { Header = name };
                    pick.Click += (_, _) => pages.AddWidget(kind, id);
                    item.Items.Add(pick);
                }
                item.IsEnabled = item.Items.Count > 0;
            }
            else if (kind is WidgetKind.Client or WidgetKind.ClientMail)
            {
                foreach (var (id, name) in pages.ClientChoices())
                {
                    var pick = new MenuItem { Header = name };
                    pick.Click += (_, _) => pages.AddWidget(kind, id);
                    item.Items.Add(pick);
                }
                item.IsEnabled = item.Items.Count > 0;
            }
            else
                item.Click += (_, _) => pages.AddWidget(kind);
            flyout.Items.Add(item);
            if (kind is WidgetKind.Client or WidgetKind.Note)
                flyout.Items.Add(new Separator());

        }
    }

    // A widget's colour: picked from a small pop-up, opened from the dot shown while arranging or by a right-click
    // on the widget's heading at any time. It is made when asked for and let go when it closes.

    private void ColourClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: WidgetViewModel widget } from)
            ShowColours(from, widget);
    }

    private void ColourAsked(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: WidgetViewModel widget } from)
            return;
        ShowColours(from, widget);
        e.Handled = true;
    }

    private void ShowColours(Control from, WidgetViewModel widget)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        // Picking a colour or a way of wearing it leaves the pop-up open, showing the new choice, to try another.
        void Fill() => flyout.Content = ColourPicker(widget, Fill, flyout.Hide);
        Fill();
        flyout.ShowAt(from);
    }

    /// <summary>What the colour pop-up shows for a widget. Also for the screenshot tool, which can't draw pop-ups.</summary>
    public Control ColourPicture(WidgetViewModel widget) => ColourPicker(widget, () => { }, () => { });

    private StackPanel ColourPicker(WidgetViewModel widget, Action changed, Action done)
    {
        const double wide = 10 * 30 + 9 * 2;
        TextBlock Label(string text)
        {
            var label = new TextBlock { Text = text };
            label.Classes.Add("label");
            label.Classes.Add("caps");
            return label;
        }
        void Wear(string? colour)
        {
            widget.Wear(colour, widget.Tint);
            changed();
        }

        // The pastels: ten to a row, each a swatch of itself.
        var pastels = new WrapPanel { Width = wide, ItemSpacing = 2, LineSpacing = 2 };
        foreach (var pastel in WidgetLooks.Pastels)
        {
            var swatch = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Child = new Controls.CategoryDot { Colour = pastel.Hex, Size = 20 } };
            var pick = new Button { Content = swatch, Padding = new Thickness(4), Width = 30, Height = 30 };
            pick.Classes.Add(string.Equals(widget.Colour, pastel.Hex, StringComparison.OrdinalIgnoreCase) ? "primary" : "quiet");
            ToolTip.SetTip(pick, pastel.Name);
            pick.Click += (_, _) => Wear(pastel.Hex);
            pastels.Children.Add(pick);
        }

        // Any colour by its code, typed or picked off the screen.
        var code = new TextBox { Text = WidgetLooks.Code(widget.Colour) is null ? "" : widget.Colour, PlaceholderText = "#C6E3F7", Width = 110 };
        var wrong = new TextBlock { Text = "Type a colour code such as #C6E3F7.", IsVisible = false, TextWrapping = TextWrapping.Wrap, MaxWidth = wide }.Sized("Text12");
        wrong.Classes.Add("short");
        void UseCode()
        {
            if (WidgetLooks.Known(code.Text) is { } known && WidgetLooks.Code(known) is not null)
                Wear(known);
            else
                wrong.IsVisible = true;
        }
        code.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                UseCode();
                e.Handled = true;
            }
        };
        var use = new Button { Content = "Use it" };
        use.Classes.Add("quiet");
        use.Click += (_, _) => UseCode();
        var fromScreen = new Button { Content = "From the screen" };
        fromScreen.Classes.Add("quiet");
        ToolTip.SetTip(fromScreen, "Click any colour on the screen, such as one from a client's logo");
        fromScreen.Click += (_, _) =>
        {
            done();
            Pages?.PickFromScreen(picked => widget.Wear(picked, widget.Tint));
        };
        var own = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { code, use, fromScreen } };

        var tints = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        foreach (var tint in Enum.GetValues<WidgetTint>())
        {
            var pick = new Button { Content = WidgetLooks.Label(tint), IsEnabled = widget.HasColour };
            pick.Classes.Add(widget.HasColour && widget.Tint == tint ? "primary" : "quiet");
            pick.Click += (_, _) =>
            {
                widget.Wear(widget.Colour, tint);
                changed();
            };
            tints.Children.Add(pick);
        }

        // Intensity: how strongly the colour shows, changed as the slider moves so the widget behind shows it.
        var shown = widget.Strength == 0 ? WidgetLooks.UsualStrength : widget.Strength;
        var percent = new TextBlock { Text = widget.Strength == 0 ? "Faint" : $"{widget.Strength}%", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Right };
        percent.Classes.Add("muted");
        var slider = new Slider
        {
            Minimum = WidgetLooks.LeastStrength, Maximum = WidgetLooks.MostStrength, Value = shown, Width = wide - 54,
            TickFrequency = WidgetLooks.StrengthStep, IsSnapToTickEnabled = true, IsEnabled = widget.HasColour && widget.Tint != WidgetTint.Line,
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || !widget.HasColour)
                return;
            var strength = WidgetLooks.Strength((int)Math.Round(slider.Value));
            if (strength == widget.Strength)
                return;
            widget.Wear(widget.Colour, widget.Tint, strength);
            percent.Text = $"{strength}%";
        };
        var intensity = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Children = { slider, percent } };

        var none = new Button { Content = "No colour", IsEnabled = widget.HasColour, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Padding = new Thickness(0) };
        none.Classes.Add("link");
        none.Click += (_, _) =>
        {
            widget.Wear(null, widget.Tint);
            done();
        };
        return new StackPanel
        {
            Spacing = 10, Margin = new Thickness(4),
            Children = { Label("Pastels"), pastels, Label("Your own colour"), own, wrong, Label("Worn as"), tints, Label("Intensity"), intensity, none },
        };
    }

    // The Files widget

    private void PickFilesFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FilesBody body)
            PickFilesFolderAsync(body).Forget("Picking a folder for the Files widget", tell: true);
    }

    private async Task PickFilesFolderAsync(FilesBody body)
    {
        if (await Webignite.Desktop.Files.Picker.Folder(this, "Folder for the Files widget", body.Folder) is { } folder)
            body.Go(folder);
    }

    // A click opens a folder in the widget; a file opens on a double-click, as in Explorer.
    private void FileLineTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileLine { IsFolder: true } line)
            line.OpenCommand.Execute(null);
    }

    private void FileLineDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileLine { IsFile: true } line)
            line.OpenCommand.Execute(null);
    }

    private void FileLineMenu(object? sender, ContextRequestedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not FileLine line)
            return;
        var open = new MenuItem { Header = line.IsFolder ? "Open the folder here" : "Open" };
        open.Click += (_, _) => line.OpenCommand.Execute(null);
        var files = new MenuItem { Header = "Show in Files" };
        files.Click += (_, _) => line.ShowInFilesCommand.Execute(null);
        var copy = new MenuItem { Header = "Copy the path" };
        copy.Click += (_, _) => CopyPathAsync(line.Path).Forget("Copying a path");
        new ContextMenu { ItemsSource = new object[] { open, files, new Separator(), copy } }.Open(sender as Control);
        e.Handled = true;
    }

    private async Task CopyPathAsync(string path)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, path);
    }

    // Dragging a file or folder out: to mail, Explorer, a browser's upload box, or the Files page.
    private Control? pressedLine;
    private PointerPressedEventArgs? pressedArgs;
    private Point pressedAt;

    private void FileLinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control line && e.GetCurrentPoint(line).Properties.IsLeftButtonPressed && Pages is { IsEditing: false })
        {
            pressedLine = line;
            pressedArgs = e;
            pressedAt = e.GetPosition(this);
        }
    }

    // A press let go without dragging is forgotten, so a later move over another line drags nothing.
    private void FileLineReleased(object? sender, PointerReleasedEventArgs e) => ForgetPress();

    private void FileLineCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ForgetPress();

    private void ForgetPress()
    {
        pressedLine = null;
        pressedArgs = null;
    }

    private void FileLineMoved(object? sender, PointerEventArgs e) => DragFileOutAsync(sender, e).Forget("Dragging a file out of the Files widget", tell: true);

    private async Task DragFileOutAsync(object? sender, PointerEventArgs e)
    {
        if (sender is null || !ReferenceEquals(sender, pressedLine) || pressedLine is not { DataContext: FileLine line } || pressedArgs is not { } start
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var moved = e.GetPosition(this) - pressedAt;
        if (Math.Abs(moved.X) < 8 && Math.Abs(moved.Y) < 8)
            return;
        pressedLine = null;
        pressedArgs = null;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        IStorageItem? item = line.IsFolder ? await storage.TryGetFolderFromPathAsync(line.Path) : await storage.TryGetFileFromPathAsync(line.Path);
        if (item is null)
            return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(item));
        try
        {
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy);
        }
        catch (InvalidOperationException problem)
        {
            Webignite.Data.AppLog.Write("Dragging a file out of the Files widget", problem);
        }
    }
}
