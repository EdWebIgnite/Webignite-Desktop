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
        DataContextChanged += (_, _) => FollowNaming();
    }

    private MyPagesViewModel? naming;

    // A page just made has its made-up name picked and the typing in it, so the first thing typed names it.
    private void FollowNaming()
    {
        if (naming is not null)
            naming.NameAsked -= AskName;
        naming = DataContext as MyPagesViewModel;
        if (naming is not null)
            naming.NameAsked += AskName;
    }

    private void AskName() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        PageName.Focus();
        PageName.SelectAll();
    }, Avalonia.Threading.DispatcherPriority.Background);

    // The page's menu, as it opens: the clients and the running projects to say the page is for, the one it is for ticked, and None.
    private void FillForMenus(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout || Pages is not { } pages)
            return;
        foreach (var item in flyout.Items.OfType<MenuItem>())
        {
            if (item.Tag as string == "client")
                item.ItemsSource = Choices(pages.ClientChoices(), pages.ForClient, pages.SetClient, "No clients yet");
            else if (item.Tag as string == "project")
                item.ItemsSource = Choices(pages.ProjectChoices(), pages.ForProject, pages.SetProject, "No projects under way");
        }
    }

    private static List<object> Choices(IReadOnlyList<(string Id, string Name)> all, string? picked, Action<string?> pick, string none)
    {
        var none_ = new MenuItem { Header = "None", ToggleType = MenuItemToggleType.Radio, IsChecked = picked is null };
        none_.Click += (_, _) => pick(null);
        var items = new List<object> { none_, new Separator() };
        if (all.Count == 0)
            items.Add(new MenuItem { Header = none, IsEnabled = false });
        foreach (var (id, name) in all)
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = id == picked };
            item.Click += (_, _) => pick(id);
            items.Add(item);
        }
        return items;
    }

    // Enter in a task's name or time on a widget is "done": the typing leaves it, which keeps what was typed.
    private void TaskNameKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter)
            return;
        Focusable = true;
        Focus();
        e.Handled = true;
    }

    // Enter in the page's name is "done": the typing leaves it, which keeps the name.
    private void PageNameKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape)
        {
            Focusable = true;
            Focus();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (naming is not null)
            naming.NameAsked -= AskName;
        naming = null;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FollowNaming();
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
        flyout.Content = ColourPicker(widget, flyout.Hide);
        flyout.ShowAt(from);
    }

    /// <summary>What the colour pop-up shows for a widget. Also for the screenshot tool, which can't draw pop-ups.</summary>
    public Control ColourPicture(WidgetViewModel widget) => ColourPicker(widget, () => { });

    private Control ColourPicker(WidgetViewModel widget, Action close) =>
        TilePicker.Build(widget.Look, widget.Wear, close, Pages is { } pages ? pages.PickFromScreen : null);

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
