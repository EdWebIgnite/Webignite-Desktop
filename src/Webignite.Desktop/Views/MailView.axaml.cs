using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TheArtOfDev.HtmlRenderer.Avalonia;
using Webignite.Core.Mail;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MailView : UserControl
{
    /// <summary>Mail being dragged out of the list, onto a folder or a category.</summary>
    private static readonly DataFormat<MailDrag> DragFormat = DataFormat.CreateInProcessFormat<MailDrag>("webignite-mail");

    private PointerPressedEventArgs? pressed;
    private Point pressedAt;
    private IReadOnlyList<MailSummary> pressedMail = [];

    public MailView()
    {
        InitializeComponent();
        // Links in mail open in the normal browser, never inside the app.
        Body.LinkClicked += (_, e) =>
        {
            e.Event.Handled = true;
            if (Uri.TryCreate(e.Event.Link, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" or "mailto")
                AppServices.OpenInBrowser(url).Forget("Opening a link");
        };
        // Watched before the list handles the press, so a drag carries everything that was picked.
        MessageList.AddHandler(PointerPressedEvent, MessagePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        MessageList.AddHandler(PointerMovedEvent, MessageMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        MessageList.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private MailViewModel? Mail => DataContext as MailViewModel;

    private void PaneResized(string pane, double width) => Mail?.Panels.Resized(pane, width);

    private void MessageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || Mail is not { } mail)
            return;
        if (mail.HandleKey(e.Key.ToString(), e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
            e.Handled = true;
    }

    private void MessageMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Mail is not { } mail || sender is not ContextMenu menu)
            return;
        var items = mail.MessageMenu();
        if (items.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        Fill(menu.Items, items);
    }

    private void CategoryFlyoutOpening(object? sender, EventArgs e)
    {
        if (Mail is { } mail && sender is MenuFlyout flyout)
            Fill(flyout.Items, mail.CategoryMenu());
    }

    private void SnoozeFlyoutOpening(object? sender, EventArgs e)
    {
        if (Mail is { } mail && sender is MenuFlyout flyout)
            Fill(flyout.Items, mail.SnoozeMenu());
    }

    private void MoveFlyoutOpening(object? sender, EventArgs e)
    {
        if (Mail is { } mail && sender is MenuFlyout flyout)
            Fill(flyout.Items, mail.MoveMenu());
    }

    /// <summary>Turns the view model's menu lines into menu items.</summary>
    private static void Fill(ItemCollection target, IReadOnlyList<MailMenuItem> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(Build(item));
    }

    private static Control Build(MailMenuItem item)
    {
        if (item.IsSeparator)
            return new Separator();
        var menuItem = new MenuItem { Header = item.Header, IsEnabled = item.IsEnabled, InputGesture = Gesture(item.Gesture) };
        if (item.Colour is { } colour)
            menuItem.Icon = new CategoryDot { Colour = colour, Size = 10 };
        else if (item.Icon is { } icon && IconConverter.Instance.Convert(icon, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) is Avalonia.Media.Geometry data)
            menuItem.Icon = new Icon { Data = data, Size = 15 };
        if (item.Checked is { } isChecked)
        {
            menuItem.ToggleType = MenuItemToggleType.CheckBox;
            menuItem.IsChecked = isChecked;
        }
        if (item.Run is { } run)
            menuItem.Click += (_, _) => run().Forget("Doing something to mail");
        foreach (var child in item.Items)
            menuItem.Items.Add(Build(child));
        return menuItem;
    }

    private static KeyGesture? Gesture(string? text)
    {
        if (text is null)
            return null;
        try
        {
            return KeyGesture.Parse(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Dragging mail onto a folder moves it; onto a category puts it in that category; onto Important marks it.

    private void MessagePressed(object? sender, PointerPressedEventArgs e)
    {
        pressed = null;
        if (Mail is not { } mail || !e.GetCurrentPoint(MessageList).Properties.IsLeftButtonPressed
            || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not MailRowViewModel row)
            return;
        var picked = mail.Selection.SelectedItems.OfType<MailRowViewModel>().ToList();
        pressedMail = picked.Contains(row) ? picked.Select(r => r.Summary).ToList() : [row.Summary];
        pressed = e;
        pressedAt = e.GetPosition(MessageList);
    }

    private async void MessageMoved(object? sender, PointerEventArgs e)
    {
        if (pressed is not { } start || !e.GetCurrentPoint(MessageList).Properties.IsLeftButtonPressed)
            return;
        var moved = e.GetPosition(MessageList) - pressedAt;
        if (Math.Abs(moved.X) < 8 && Math.Abs(moved.Y) < 8)
            return;
        pressed = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormat, new MailDrag(pressedMail)));
        try
        {
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
        }
        catch (InvalidOperationException e2)
        {
            Webignite.Data.AppLog.Write("Dragging mail", e2);
        }
    }

    private static MailDrag? Dragged(DragEventArgs e) => e.DataTransfer.TryGetValue(DragFormat);

    private void FolderDragOver(object? sender, DragEventArgs e)
    {
        var folder = (sender as Control)?.DataContext as FolderNodeViewModel;
        var ok = folder is not null && Dragged(e) is { } drag && (folder.Kind switch
        {
            FolderKind.Folder => drag.Mail.Any(m => m.AccountId == folder.Mailbox.Id && m.Folder != folder.Path),
            FolderKind.Important => true,
            _ => false,
        });
        e.DragEffects = ok ? (folder!.Kind == FolderKind.Folder ? DragDropEffects.Move : DragDropEffects.Link) : DragDropEffects.None;
        if (folder is not null)
            folder.IsDropTarget = ok;
        e.Handled = true;
    }

    private void FolderDragLeave(object? sender, DragEventArgs e)
    {
        if ((sender as Control)?.DataContext is FolderNodeViewModel folder)
            folder.IsDropTarget = false;
    }

    private void FolderDrop(object? sender, DragEventArgs e)
    {
        if ((sender as Control)?.DataContext is not FolderNodeViewModel folder || Mail is not { } mail || Dragged(e) is not { } drag)
            return;
        folder.IsDropTarget = false;
        e.Handled = true;
        if (folder.Kind == FolderKind.Important)
            mail.SetImportant(drag.Mail, true).Forget("Marking mail important");
        else if (folder.Kind == FolderKind.Folder)
            mail.MoveTo(folder, drag.Mail).Forget("Moving mail");
    }

    private void CategoryDragOver(object? sender, DragEventArgs e)
    {
        var category = (sender as Control)?.DataContext as CategoryNodeViewModel;
        var ok = category is not null && Dragged(e) is not null;
        e.DragEffects = ok ? DragDropEffects.Link : DragDropEffects.None;
        if (category is not null)
            category.IsDropTarget = ok;
        e.Handled = true;
    }

    private void CategoryDragLeave(object? sender, DragEventArgs e)
    {
        if ((sender as Control)?.DataContext is CategoryNodeViewModel category)
            category.IsDropTarget = false;
    }

    private void CategoryDrop(object? sender, DragEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CategoryNodeViewModel category || Mail is not { } mail || Dragged(e) is not { } drag)
            return;
        category.IsDropTarget = false;
        e.Handled = true;
        mail.SetCategory(category.Keyword, true, drag.Mail).Forget("Adding mail to a category");
    }

    private async void ChooseSignatureImage(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MailSetupViewModel setup || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a signature picture",
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            setup.SetSignatureImage(path);
    }
}

/// <summary>What travels with a drag out of the message list.</summary>
public sealed record MailDrag(IReadOnlyList<MailSummary> Mail);
