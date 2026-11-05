using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The Files page: keys, the right-click menu, dragging files in and out (to Explorer, Outlook or a folder here),
/// the clipboard, and drawing thumbnails only for what is on screen.
/// </summary>
public partial class FilesView : FullBleedView
{
    private FilesViewModel? watching;
    private bool syncing;
    private PointerPressedEventArgs? pressed;
    private Point pressedAt;
    private IReadOnlyList<string> pressedPaths = [];
    private IReadOnlyList<string> dragging = [];
    private Point? menuAt;

    public FilesView()
    {
        InitializeComponent();
        FileList.SelectionChanged += Picked;
        FileList.ContainerPrepared += Prepared;
        FileList.ContainerClearing += Clearing;
        FileList.DoubleTapped += DoubleTappedRow;
        FileList.SizeChanged += (_, e) =>
        {
            if (Files is { } files)
                files.IsNarrow = e.NewSize.Width < 820;
        };
        FileList.AddHandler(PointerReleasedEvent, ColumnClicked, RoutingStrategies.Bubble, handledEventsToo: true);
        // Space is Quick Look, caught before the list takes it for picking.
        FileList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox && Files is { } files)
            {
                files.ToggleQuickLook();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        FileList.ContextRequested += MenuRequested;
        FileList.AddHandler(PointerPressedEvent, RowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileList.AddHandler(PointerMovedEvent, RowMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileList.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileList.AddHandler(DragDrop.DragOverEvent, DraggedOver);
        FileList.AddHandler(DragDrop.DropEvent, Dropped);
        AddHandler(KeyDownEvent, Keys, RoutingStrategies.Bubble);
    }

    private FilesViewModel? Files => DataContext as FilesViewModel;

    private void PaneResized(string pane, double width) => Files?.Panels.Resized(pane, width);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (watching is not null)
        {
            watching.Restored = null;
            watching.RenameStarted = null;
            watching.PropertyChanged -= LookChanged;
        }
        watching = Files;
        if (watching is not null)
        {
            watching.Restored = Restore;
            watching.RenameStarted = FocusRename;
            watching.PropertyChanged += LookChanged;
        }
    }

    // Selection

    private void Picked(object? sender, SelectionChangedEventArgs e)
    {
        if (syncing || Files is not { } files)
            return;
        // Group headings can't be picked.
        foreach (var heading in e.AddedItems.OfType<FileHeaderRow>().ToList())
            FileList.Selection.Deselect(files.Items.IndexOf(heading));
        files.Selected = [.. FileList.Selection.SelectedItems.OfType<FileRowViewModel>()];
        files.PickChanged();
    }

    private void Restore(IReadOnlyList<FileRowViewModel> rows)
    {
        if (Files is not { } files)
            return;
        syncing = true;
        try
        {
            FileList.Selection.Clear();
            foreach (var row in rows)
                FileList.Selection.Select(files.Items.IndexOf(row));
        }
        finally
        {
            syncing = false;
        }
        if (rows.Count > 0)
            FileList.ScrollIntoView(rows[0]);
    }

    private static FileRowViewModel? RowAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as FileRowViewModel;

    private void DoubleTappedRow(object? sender, TappedEventArgs e)
    {
        if (Files is not { } files || RowAt(e.Source) is not { IsRenaming: false } row)
            return;
        e.Handled = true;
        // Ctrl+double-click opens a folder in a tab of its own, as in Explorer.
        if (row.IsFolder && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            files.OpenInNewTab(row);
        else
            files.Open(row);
    }

    // Keys

    private void Keys(object? sender, KeyEventArgs e)
    {
        if (Files is not { } files || e.Source is TextBox)
            return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (files.IsPreviewing)
        {
            if (e.Key is Key.Escape or Key.Back)
            {
                files.ClosePreviewCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }
        if (files.IsQuickLooking && e.Key is Key.Escape)
        {
            files.CloseQuickLookCommand.Execute(null);
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.F when ctrl:
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case Key.Escape when files.IsSearching:
                files.ClearSearchCommand.Execute(null);
                break;
            // Column view: Right opens the picked folder in the next column, Left goes back a column.
            case Key.Right when files.IsColumns && !alt && files.Selected is [{ IsFolder: true } folder]:
                files.GoTo(folder.Entry.Path);
                FocusFirstRow();
                break;
            case Key.Left when files.IsColumns && !alt && files.CanGoUp:
                files.UpAndPick();
                break;
            case Key.Enter when alt && OperatingSystem.IsWindows():
                files.PropertiesCommand.Execute(null);
                break;
            case Key.Enter when !ctrl && !alt:
                files.OpenPickedCommand.Execute(null);
                break;
            case Key.F2:
                files.StartBulkRename();
                break;
            case Key.Delete:
                files.DeleteCommand.Execute(null);
                break;
            case Key.F5:
                files.Refresh();
                break;
            case Key.C when ctrl && shift:
                CopyPath();
                break;
            case Key.C when ctrl:
                files.CopyCommand.Execute(null);
                ShareWithWindows().Forget("Copying the files");
                break;
            case Key.X when ctrl:
                files.CutCommand.Execute(null);
                ShareWithWindows().Forget("Cutting the files");
                break;
            case Key.V when ctrl:
                PasteAsync().Forget("Pasting the files", tell: true);
                break;
            case Key.Z when ctrl:
                if (files.UndoCommand.CanExecute(null))
                    files.UndoCommand.Execute(null);
                break;
            case Key.N when ctrl && shift:
                files.NewFolderCommand.Execute(null);
                break;
            case Key.L when ctrl:
                StartTypingAddress();
                break;
            case Key.Left when alt:
            case Key.Back when !alt:
                if (files.BackCommand.CanExecute(null))
                    files.BackCommand.Execute(null);
                break;
            case Key.Right when alt:
                if (files.ForwardCommand.CanExecute(null))
                    files.ForwardCommand.Execute(null);
                break;
            case Key.Up when alt:
                if (files.UpCommand.CanExecute(null))
                    files.UpCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // The address bar

    private void AddressPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            StartTypingAddress();
    }

    private void StartTypingAddress()
    {
        Files?.EditAddressCommand.Execute(null);
        Dispatcher.UIThread.Post(() =>
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    // A middle click on a folder in the address opens it in a tab of its own.
    private void CrumbReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle && (sender as Control)?.DataContext is FileCrumb crumb)
        {
            Files?.OpenInNewTab(crumb.Path);
            e.Handled = true;
        }
    }

    private void AddressKeys(object? sender, KeyEventArgs e)
    {
        if (Files is not { } files)
            return;
        if (e.Key == Key.Enter)
        {
            files.GoToAddressCommand.Execute(null);
            FileList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            files.StopEditingAddressCommand.Execute(null);
            FileList.Focus();
            e.Handled = true;
        }
    }

    private void AddressLost(object? sender, RoutedEventArgs e) => Files?.StopEditingAddressCommand.Execute(null);

    // Renaming

    private void FocusRename(FileRowViewModel row) => Dispatcher.UIThread.Post(() =>
    {
        FileList.ScrollIntoView(row);
        if (FileList.ContainerFromItem(row)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("rename")) is not { } box)
            return;
        box.Focus();
        // The name is picked without its extension, so typing keeps ".pdf".
        var dot = row.IsFolder ? -1 : row.RenameText.LastIndexOf('.');
        box.SelectionStart = 0;
        box.SelectionEnd = dot > 0 ? dot : row.RenameText.Length;
    }, DispatcherPriority.Loaded);

    private void RenameKeys(object? sender, KeyEventArgs e)
    {
        if (Files is not { } files || (sender as Control)?.DataContext is not FileRowViewModel row)
            return;
        if (e.Key == Key.Enter)
        {
            if (files.FinishRename(row))
                FileList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FilesViewModel.CancelRename(row);
            FileList.Focus();
            e.Handled = true;
        }
    }

    // Clicking away keeps the new name; one that can't be used is dropped, as in Explorer.
    private void RenameLost(object? sender, RoutedEventArgs e)
    {
        if (Files is { } files && (sender as Control)?.DataContext is FileRowViewModel { IsRenaming: true } row && !files.FinishRename(row))
            FilesViewModel.CancelRename(row);
    }

    // The right-click menu

    private void MenuRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Files is not { } files)
            return;
        e.Handled = true;
        menuAt = e.TryGetPosition(FileList, out var at) ? at : null;
        var row = RowAt(e.Source);
        if (row is null)
            FileList.Selection.Clear();
        else if (!files.Selected.Contains(row))
            Restore([row]);
        files.Selected = [.. FileList.Selection.SelectedItems.OfType<FileRowViewModel>()];
        var menu = new ContextMenu { ItemsSource = MenuItems(files) };
        menu.Open(FileList);
    }

    /// <summary>The right-click menu for what is picked, or for the folder when nothing is. Public for the screenshot checks.</summary>
    public List<Control> MenuItems(FilesViewModel files)
    {
        var picked = files.Selected;
        var windows = OperatingSystem.IsWindows();
        var items = new List<Control>();
        if (picked.Count > 0)
        {
            items.Add(Item("Open", "Enter", () => files.OpenPickedCommand.Execute(null)));
            if (picked.Any(r => r.IsFolder))
                items.Add(Item("Open in a new tab (Ctrl+double-click)", null, () => files.OpenPickedInNewTabCommand.Execute(null)));
            if (picked is [{ IsFolder: true } newWindow])
                items.Add(Item("Open in a new window", null, () => files.OpenInNewWindow(newWindow)));
            if (windows && picked.Count == 1 && !picked[0].IsFolder)
                items.Add(Item("Open with…", null, () => files.OpenWithCommand.Execute(null)));
            items.Add(Item("Quick Look", "Space", files.ToggleQuickLook));
            if (files.CanMarkup)
                items.Add(Item("Draw on it in Mark-up", null, files.MarkupPicked));
            if (picked.All(r => !r.IsFolder && (Webignite.Core.Files.FileKinds.IsPdf(r.Name) || Webignite.Core.Files.QuickActions.CanRotate(r.Name))) && picked.Any(r => Webignite.Core.Files.FileKinds.IsPdf(r.Name)))
                items.Add(Item("PDF tools", null, () => App.Services?.Go.Pdf([.. picked.Select(r => r.Entry.Path)])));
            if (files.IsGathered && picked.Count == 1)
                items.Add(Item("Show in its folder", null, () => files.ShowInItsFolder(picked[0])));
            items.Add(TagsMenu(files));
            if (picked.Any(r => !r.IsFolder))
                items.Add(Item(picked.Count(r => !r.IsFolder) == 1 ? "Mail it" : "Mail them", null, () => files.MailPicked().Forget("Putting the files in a mail", tell: true)));
            items.Add(LinkToClientMenu(files));
            if (QuickActionsMenu(files) is { } quick)
                items.Add(quick);
            if (picked is [{ IsFolder: true } pin])
                items.Add(files.IsFavourite(pin.Entry.Path)
                    ? Item("Remove from Favourites", null, () => files.RemoveFavourite(pin.Entry.Path))
                    : Item("Add to Favourites", null, () => files.AddFavourite(pin.Entry.Path)));
            items.Add(new Separator());
            items.Add(Item("Cut", "Ctrl+X", () =>
            {
                files.CutCommand.Execute(null);
                ShareWithWindows().Forget("Cutting the files");
            }));
            items.Add(Item("Copy", "Ctrl+C", () =>
            {
                files.CopyCommand.Execute(null);
                ShareWithWindows().Forget("Copying the files");
            }));
        }
        items.Add(Item("Paste", "Ctrl+V", () => PasteAsync().Forget("Pasting the files", tell: true)));
        items.Add(Item("Copy as path", "Ctrl+Shift+C", CopyPath));
        items.Add(new Separator());
        if (picked.Count > 0)
        {
            items.Add(Item(picked.Count == 1 ? "Rename" : $"Rename {picked.Count} items…", "F2", files.StartBulkRename));
            items.Add(Item("Delete", "Delete", () => files.DeleteCommand.Execute(null)));
            items.Add(new Separator());
        }
        items.Add(Item("New folder", "Ctrl+Shift+N", () => files.NewFolderCommand.Execute(null)));
        if (files.CanUndo)
            items.Add(Item(files.UndoText, "Ctrl+Z", () => files.UndoCommand.Execute(null)));
        // On empty space: how the folder is shown, and the folder itself.
        if (picked.Count == 0)
        {
            items.Add(new Separator());
            items.Add(ViewMenu(files));
            items.Add(Item("Refresh", "F5", files.Refresh));
            if (System.IO.Directory.Exists(files.Folder))
                items.Add(files.FolderIsFavourite
                    ? Item("Remove this folder from Favourites", null, () => files.RemoveFavourite(files.Folder))
                    : Item("Add this folder to Favourites", null, () => files.AddFavourite(files.Folder)));
            if (windows)
                items.Add(Item("Open in Terminal", null, files.OpenTerminal));
        }
        if (windows)
        {
            items.Add(new Separator());
            items.Add(Item("Show in Explorer", null, () => files.ShowInExplorerCommand.Execute(null)));
            items.Add(Item("Properties", "Alt+Enter", () => files.PropertiesCommand.Execute(null)));
            items.Add(Item("More options", null, () => WindowsMenu(files)));
        }
        return items;
    }

    // View, sort and group, as on the toolbar, for right-clicking empty space.
    private static MenuItem ViewMenu(FilesViewModel files)
    {
        var menu = new MenuItem { Header = "View", Icon = new Controls.Icon { Data = Icon("Tiles"), Size = 15 } };
        foreach (var (view, name) in new[] { (Webignite.Core.Files.FileView.Details, "Details"), (Webignite.Core.Files.FileView.List, "List"),
                     (Webignite.Core.Files.FileView.Thumbnails, "Thumbnails"), (Webignite.Core.Files.FileView.Columns, "Columns") })
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = files.View == view, GroupName = "view" };
            item.Click += (_, _) => files.ShowAsCommand.Execute(view);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var sort = new MenuItem { Header = "Sort by" };
        foreach (var (by, name) in new[] { (Webignite.Core.Files.FileSort.Name, "Name"), (Webignite.Core.Files.FileSort.Modified, "Date changed"),
                     (Webignite.Core.Files.FileSort.Size, "Size"), (Webignite.Core.Files.FileSort.Kind, "Kind") })
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = files.Sort == by, GroupName = "sort" };
            item.Click += (_, _) => files.SortByCommand.Execute(by);
            sort.Items.Add(item);
        }
        menu.Items.Add(sort);
        var group = new MenuItem { Header = "Group by" };
        foreach (var (by, name) in new[] { (Webignite.Core.Files.FileGrouping.None, "Nothing"), (Webignite.Core.Files.FileGrouping.Kind, "Kind"),
                     (Webignite.Core.Files.FileGrouping.Date, "Date"), (Webignite.Core.Files.FileGrouping.Tag, "Tag") })
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = files.Grouping == by, GroupName = "group" };
            item.Click += (_, _) => files.GroupByCommand.Execute(by);
            group.Items.Add(item);
        }
        menu.Items.Add(group);
        return menu;
    }

    // Link the picked files to a client's page; the files stay where they are.
    private static MenuItem LinkToClientMenu(FilesViewModel files)
    {
        var menu = new MenuItem { Header = "Link to a client", Icon = new Controls.Icon { Data = Icon("Clients"), Size = 15 } };
        foreach (var client in files.ClientsToLink())
            menu.Items.Add(Item(client.Name, null, () => files.LinkPickedTo(client)));
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No clients yet", IsEnabled = false });
        return menu;
    }

    // Quick actions: only those that fit what is picked.
    private static MenuItem? QuickActionsMenu(FilesViewModel files)
    {
        var menu = new MenuItem { Header = "Quick actions", Icon = new Controls.Icon { Data = Icon("Tools"), Size = 15 } };
        if (files.PickedPictures.Count > 0)
        {
            menu.Items.Add(Item("Turn left", null, () => files.RotateLeftCommand.Execute(null)));
            menu.Items.Add(Item("Turn right", null, () => files.RotateRightCommand.Execute(null)));
            menu.Items.Add(Item("Make a smaller copy for a website", null, () => files.ShrinkCommand.Execute(null)));
        }
        if (files.CanCombine && !files.IsGathered)
            menu.Items.Add(Item("Put into one PDF", null, () => files.CombineCommand.Execute(null)));
        if (!files.IsGathered)
            menu.Items.Add(Item("Zip", null, () => files.ZipCommand.Execute(null)));
        return menu.Items.Count > 0 ? menu : null;
    }

    // Tags: each one ticked when everything picked has it; picking it puts it on or takes it off.
    private static MenuItem TagsMenu(FilesViewModel files)
    {
        var menu = new MenuItem { Header = "Tags", Icon = new Controls.Icon { Data = Icon("Tag"), Size = 15 } };
        foreach (var tag in files.AllTags)
        {
            var tagItem = new MenuItem
            {
                Header = tag.Name,
                Icon = new Controls.CategoryDot { Colour = tag.Colour, Size = 10 },
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = files.Selected.Count > 0 && files.Selected.All(r => r.Tags.Any(t => t.Id == tag.Id)),
            };
            tagItem.Click += (_, _) => files.ToggleTagCommand.Execute(tag);
            menu.Items.Add(tagItem);
        }
        if (files.AllTags.Count > 0)
            menu.Items.Add(new Separator());
        menu.Items.Add(Item("Make or change tags…", null, () => App.Services?.Go.Settings("files")));
        return menu;
    }

    private static Avalonia.Media.Geometry? Icon(string name) =>
        Controls.IconConverter.Instance.Convert(name, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) as Avalonia.Media.Geometry;

    // The Show button: one kind of file, files from a time, or everything.
    private void ShowFilterOpening(object? sender, EventArgs e)
    {
        if (Files is not { } files || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        foreach (var kind in Enum.GetValues<Webignite.Core.Files.KindFilter>())
        {
            var item = new MenuItem { Header = Webignite.Core.Files.FileFilters.Name(kind), ToggleType = MenuItemToggleType.Radio, IsChecked = files.ShowKind == kind, GroupName = "kind" };
            item.Click += (_, _) => files.ShowOnlyCommand.Execute(kind);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        foreach (var date in Enum.GetValues<Webignite.Core.Files.DateFilter>())
        {
            var item = new MenuItem { Header = Webignite.Core.Files.FileFilters.Name(date), ToggleType = MenuItemToggleType.Radio, IsChecked = files.ShowDate == date, GroupName = "date" };
            item.Click += (_, _) => files.ShowFromCommand.Execute(date);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("Show everything", null, () => files.ShowEverythingCommand.Execute(null)));
    }

    // The Tags button: all files, or only those with one tag.
    private void TagFilterOpening(object? sender, EventArgs e)
    {
        if (Files is not { } files || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        var all = new MenuItem { Header = "All files", ToggleType = MenuItemToggleType.Radio, IsChecked = !files.IsFiltered };
        all.Click += (_, _) => files.FilterByCommand.Execute(null);
        flyout.Items.Add(all);
        flyout.Items.Add(new Separator());
        foreach (var tag in files.AllTags)
        {
            var only = new MenuItem
            {
                Header = tag.Name,
                Icon = new Controls.CategoryDot { Colour = tag.Colour, Size = 10 },
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = files.FilterTag?.Id == tag.Id,
            };
            only.Click += (_, _) => files.FilterByCommand.Execute(tag);
            flyout.Items.Add(only);
        }
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("Make or change tags…", null, () => App.Services?.Go.Settings("files")));
    }

    private static MenuItem Item(string header, string? keys, Action run)
    {
        var item = new MenuItem { Header = header, InputGesture = keys is null ? null : KeyGesture.Parse(keys) };
        item.Click += (_, _) => run();
        return item;
    }

    // Windows' own right-click menu, with what other programs add there (7-Zip, OneDrive, antivirus).
    private void WindowsMenu(FilesViewModel files)
    {
        if (!OperatingSystem.IsWindows() || TopLevel.GetTopLevel(this) is not { } top || top.TryGetPlatformHandle()?.Handle is not { } handle)
            return;
        var paths = files.Selected.Count > 0 ? files.SelectedPaths : [files.Folder];
        var at = FileList.PointToScreen(menuAt ?? new Point(FileList.Bounds.Width / 2, 40));
        if (!WindowsShell.ShowMenu(paths, handle, at.X, at.Y))
            files.ShowInExplorerCommand.Execute(null);
    }

    // The clipboard

    private void CopyPath()
    {
        if (Files is { } files && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            clipboard.SetTextAsync(files.PathsToCopy()).Forget("Copying the path");
    }

    // Copied or cut files go on Windows' clipboard too, so they paste into Explorer, Outlook or WhatsApp.
    private async Task ShareWithWindows()
    {
        if (Files is not { } files || files.Selected.Count == 0 || TopLevel.GetTopLevel(this) is not { Clipboard: { } clipboard } top)
            return;
        var items = await StorageItems(top.StorageProvider, files.SelectedPaths);
        if (items.Count > 0)
            await clipboard.SetFilesAsync(items);
    }

    private async Task PasteAsync()
    {
        if (Files is not { } files)
            return;
        IReadOnlyList<string> fromWindows = [];
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && await clipboard.TryGetFilesAsync() is { } items)
            fromWindows = [.. items.Select(i => i.TryGetLocalPath()).OfType<string>()];
        // Files copied in another program since: those win over what was copied here.
        var app = App.Services?.FileClipboard;
        if (app is not null && fromWindows.Count > 0 && !fromWindows.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(app.Paths))
            app.Clear();
        files.Paste(fromWindows);
    }

    private static async Task<IReadOnlyList<IStorageItem>> StorageItems(IStorageProvider storage, IEnumerable<string> paths)
    {
        var items = new List<IStorageItem>();
        foreach (var path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await storage.TryGetFolderFromPathAsync(path)
                : await storage.TryGetFileFromPathAsync(path);
            if (item is not null)
                items.Add(item);
        }
        return items;
    }

    // Dragging out: to Explorer, Outlook, a browser's upload box, or a folder here.

    private void RowPressed(object? sender, PointerPressedEventArgs e)
    {
        pressed = null;
        var point = e.GetCurrentPoint(FileList).Properties;
        var row = RowAt(e.Source);
        if (point.IsMiddleButtonPressed && row is { IsFolder: true } && Files is { } files)
        {
            files.OpenInNewTab(row);
            e.Handled = true;
            return;
        }
        if (!point.IsLeftButtonPressed || row is null || row.IsRenaming || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;
        var picked = Files?.Selected ?? [];
        pressedPaths = picked.Contains(row) ? [.. picked.Select(r => r.Entry.Path)] : [row.Entry.Path];
        pressed = e;
        pressedAt = e.GetPosition(FileList);
    }

    private void RowMoved(object? sender, PointerEventArgs e) => DragOutAsync(e).Forget("Dragging the files", tell: true);

    private async Task DragOutAsync(PointerEventArgs e)
    {
        if (pressed is not { } start || !e.GetCurrentPoint(FileList).Properties.IsLeftButtonPressed)
            return;
        var moved = e.GetPosition(FileList) - pressedAt;
        if (Math.Abs(moved.X) < 8 && Math.Abs(moved.Y) < 8)
            return;
        pressed = null;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var items = await StorageItems(storage, pressedPaths);
        if (items.Count == 0)
            return;
        var data = new DataTransfer();
        foreach (var item in items)
            data.Add(DataTransferItem.CreateFile(item));
        dragging = pressedPaths;
        try
        {
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (InvalidOperationException problem)
        {
            Webignite.Data.AppLog.Write("Dragging files", problem);
        }
        finally
        {
            dragging = [];
            foreach (var item in items)
                item.Dispose();
        }
    }

    // Dropping in: onto a folder in the list, into that folder; anywhere else, into the folder shown.
    // Copied, or moved with Shift held (or when dragged between folders on the same drive here, as Explorer does).

    private (string Into, IReadOnlyList<string> Paths, bool Move)? DropPlan(DragEventArgs e)
    {
        if (Files is not { } files || e.DataTransfer.TryGetFiles() is not { } dropped)
            return null;
        var paths = dropped.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0)
            return null;
        var into = RowAt(e.Source) is { IsFolder: true } folder && !paths.Contains(folder.Entry.Path, StringComparer.OrdinalIgnoreCase)
            ? folder.Entry.Path
            : files.Folder;
        var ours = dragging.Count > 0;
        // Dragged from this folder onto its own background: nothing to do.
        if (ours && string.Equals(into, files.Folder, StringComparison.OrdinalIgnoreCase)
            && paths.All(p => string.Equals(Path.GetDirectoryName(p.TrimEnd('\\', '/')), into.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            return null;
        var sameDrive = paths.All(p => string.Equals(Path.GetPathRoot(p), Path.GetPathRoot(into), StringComparison.OrdinalIgnoreCase));
        var move = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (ours && sameDrive && !e.KeyModifiers.HasFlag(KeyModifiers.Control));
        return (into, paths, move);
    }

    private void DraggedOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DropPlan(e) is { } plan ? plan.Move ? DragDropEffects.Move : DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Dropped(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Files is { } files && DropPlan(e) is { } plan)
            files.Bring(plan.Paths, plan.Move, plan.Into);
    }

    // Thumbnails: drawn when a picture scrolls into sight, at the size it shows on this screen.

    private void Prepared(object? sender, ContainerPreparedEventArgs e)
    {
        var heading = e.Container.DataContext is FileHeaderRow;
        e.Container.Focusable = !heading;
        e.Container.IsHitTestVisible = !heading;
        e.Container.EffectiveViewportChanged -= InSight;
        if (!heading)
            e.Container.EffectiveViewportChanged += InSight;
    }

    private void Clearing(object? sender, ContainerClearingEventArgs e) => e.Container.EffectiveViewportChanged -= InSight;

    private void InSight(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is Control { DataContext: FileRowViewModel { IsPicture: true } row } container && Files is { IsThumbnails: true }
            && e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Intersects(new Rect(container.Bounds.Size)))
            row.LoadThumbnail(ThumbnailWidth());
    }

    private int ThumbnailWidth() => (int)Math.Ceiling((Files?.ThumbnailSize ?? 120) * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));

    private void LookChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Column view keeps the newest column in sight.
        if (e.PropertyName is nameof(FilesViewModel.Folder) or nameof(FilesViewModel.View) && Files is { IsColumns: true })
            Dispatcher.UIThread.Post(() => ColumnsScroller.Offset = new Vector(double.MaxValue, ColumnsScroller.Offset.Y), DispatcherPriority.Loaded);
        // A long path keeps the folder you're in on screen; the start of the path is what scrolls out of sight.
        if (e.PropertyName == nameof(FilesViewModel.Folder))
            Dispatcher.UIThread.Post(() => CrumbScroll.Offset = new Vector(double.MaxValue, 0), DispatcherPriority.Loaded);
        if (e.PropertyName is not (nameof(FilesViewModel.View) or nameof(FilesViewModel.ThumbnailSize)) || Files is not { IsThumbnails: true })
            return;
        // Once laid out in the new view, draw what is now in sight.
        Dispatcher.UIThread.Post(() =>
        {
            var viewport = FileList.Bounds;
            foreach (var container in FileList.GetRealizedContainers())
                if (container.DataContext is FileRowViewModel { IsPicture: true } row && container.TranslatePoint(default, FileList) is { } at
                    && new Rect(at, container.Bounds.Size).Intersects(new Rect(viewport.Size)))
                    row.LoadThumbnail(ThumbnailWidth());
        }, DispatcherPriority.Loaded);
    }

    // Search box: Enter searches straight away, Esc empties it, Down goes to the results.
    private void SearchKeys(object? sender, KeyEventArgs e)
    {
        if (Files is not { } files)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                files.StartSearch();
                break;
            case Key.Escape:
                files.ClearSearchCommand.Execute(null);
                FileList.Focus();
                break;
            case Key.Down:
                FocusFirstRow();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void FocusFirstRow() => Dispatcher.UIThread.Post(() =>
    {
        if (Files is { } files && files.Items.OfType<FileRowViewModel>().FirstOrDefault() is { } first && files.Selected.Count == 0)
            Restore([first]);
        FileList.Focus();
    }, DispatcherPriority.Loaded);

    // Quick Look closes on a click outside it, as in Finder.
    private void QuickLookBackdrop(object? sender, PointerPressedEventArgs e)
    {
        Files?.CloseQuickLookCommand.Execute(null);
        FileList.Focus();
    }

    private void QuickLookCard(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    // Column view: a click on a folder opens it in the next column.
    private void ColumnClicked(object? sender, PointerReleasedEventArgs e)
    {
        if (Files is not { IsColumns: true } files || e.InitialPressMouseButton != MouseButton.Left
            || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            || RowAt(e.Source) is not { IsFolder: true, IsRenaming: false } row)
            return;
        Dispatcher.UIThread.Post(() => files.GoTo(row.Entry.Path));
    }

    // A favourite in the side list can be taken off from its right-click menu.
    private void PlaceMenu(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: FilePlace { IsFavourite: true } place } button)
            return;
        e.Handled = true;
        new ContextMenu { ItemsSource = new[] { Item("Remove from Favourites", null, () => place.RemoveFavouriteCommand.Execute(null)) } }.Open(button);
    }
}
