using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Webignite.Core.Writing;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

namespace Webignite.Desktop.Views;

public partial class NotesView : FullBleedView
{
    /// <summary>Folders sit in under their notebook.</summary>
    public static readonly IValueConverter FolderIndent = new FuncValueConverter<bool, Thickness>(folder => folder ? new Thickness(16, 0, 0, 0) : default);

    public static readonly IValueConverter NotebookWeight = new FuncValueConverter<bool, FontWeight>(notebook => notebook ? FontWeight.SemiBold : FontWeight.Normal);

    private bool showingFormat;

    public NotesView()
    {
        InitializeComponent();
        Editor.SelectionChanged += (_, _) => ShowFormat();
        Editor.AddHandler(KeyDownEvent, EditorKeys, RoutingStrategies.Tunnel);
        Editor.LinkOpened += OpenLink;
        LinkBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) { AddLink(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelLink(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, PageKeys, RoutingStrategies.Tunnel);
        foreach (var colour in MailColours.Highlight)
        {
            var swatch = new Button
            {
                Classes = { "shade" },
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(0),
                Focusable = false,
                Tag = "highlight:" + colour,
                Content = new Border { Background = Brush.Parse(colour), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Width = 20, Height = 20 },
            };
            ToolTip.SetTip(swatch, MailColours.Name(colour));
            swatch.Click += Format;
            HighlightList.Items.Add(swatch);
        }
        ShowFormat();
    }

    private NotesViewModel? Notes => DataContext as NotesViewModel;

    private void PaneResized(string pane, double width) => Notes?.Panels.Resized(pane, width);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Notes is { } notes && this.IsAttachedToVisualTree())
            notes.FocusTitle = FocusTitle;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Editor.Spelling = App.Services?.Spelling;
        if (Notes is { } notes)
            notes.FocusTitle = FocusTitle;
    }

    private void FocusTitle() => Dispatcher.UIThread.Post(() => TitleBox.Focus());

    // The page can outlive this screen (moved to another window), so it lets go of it.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Notes is { } notes)
            notes.FocusTitle = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Lists: one-way, as a list being taken down clears its pick, which would otherwise close the note.

    private void PlacePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (PlaceList.SelectedItem is NotePlaceRow place)
            Notes?.PickPlace(place);
    }

    private void NotePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (NoteList.SelectedItem is NoteRow row)
            Notes?.PickRow(row);
    }

    private void ClientChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (ClientBox.SelectedItem is Webignite.Core.Clients.Client client)
            Notes?.ClientPicked(client);
    }

    // A note's right-click: it opens first, then pin, move, a quick note to its client, delete.
    private void NoteMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Notes is not { } notes || sender is not ContextMenu menu || menu.DataContext is not NoteRow row)
            return;
        menu.Items.Clear();
        void Add(ItemsControl into, string header, Action run)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) =>
            {
                notes.OpenNote(row.Note.Id);
                run();
            };
            into.Items.Add(item);
        }
        Add(menu, "Open", () => { });
        if (row.Note.Deleted is null)
        {
            Add(menu, row.Pinned ? "Unpin" : "Pin to the top", () => notes.TogglePinCommand.Execute(null));
            var move = new MenuItem { Header = "Move to" };
            foreach (var target in notes.MoveTargets)
                Add(move, target.Label, () => notes.MoveTo(target));
            menu.Items.Add(move);
            menu.Items.Add(new Separator());
            Add(menu, "Delete", () => notes.DeleteOpenCommand.Execute(null));
        }
        else
        {
            Add(menu, "Put it back", () => notes.RestoreOpenCommand.Execute(null));
            Add(menu, "Delete for good", () => notes.PurgeOpenCommand.Execute(null));
        }
    }

    // A notebook's or folder's right-click: it is picked first, then a new folder, rename, remove.
    private void PlaceMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Notes is not { } notes || sender is not ContextMenu menu || menu.DataContext is not NotePlaceRow place)
            return;
        menu.Items.Clear();
        void Add(string header, Action run)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) =>
            {
                notes.PickPlace(place);
                run();
            };
            menu.Items.Add(item);
        }
        Add("New note here", () => notes.NewNoteCommand.Execute(null));
        if (place.Kind is Webignite.Core.Notes.NotePlace.Notebook or Webignite.Core.Notes.NotePlace.Folder)
        {
            Add("New folder", () => notes.AddFolderCommand.Execute(null));
            Add("Rename", () => notes.RenamePlaceCommand.Execute(null));
            Add(place.IsFolder ? "Remove folder" : "Remove notebook", () => notes.RemovePlaceCommand.Execute(null));
        }
        if (place.Kind == Webignite.Core.Notes.NotePlace.Deleted)
            Add("Empty Deleted", () => notes.EmptyDeletedCommand.Execute(null));
        menu.Items.Add(new Separator());
        var newBook = new MenuItem { Header = "New notebook" };
        newBook.Click += (_, _) => notes.AddNotebookCommand.Execute(null);
        menu.Items.Add(newBook);
    }

    private void MoveMenuOpening(object? sender, EventArgs e)
    {
        if (Notes is not { } notes || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        foreach (var target in notes.MoveTargets)
        {
            var item = new MenuItem { Header = target.Label };
            item.Click += (_, _) => notes.MoveTo(target);
            flyout.Items.Add(item);
        }
    }

    // A web address opens in the browser; an email address starts a mail here.
    private static void OpenLink(string link)
    {
        if (link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            App.Services?.Go.WriteMail([new Webignite.Core.Mail.MailPerson(null, Uri.UnescapeDataString(link[7..].Split('?')[0]))]);
        else if (Uri.TryCreate(link, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            AppServices.OpenInBrowser(url).Forget("Opening a link");
    }

    // Keys

    private void PageKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void SearchKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Notes is { } notes)
        {
            notes.Query = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Down && NoteList.ItemCount > 0)
        {
            NoteList.SelectedIndex = Math.Max(NoteList.SelectedIndex, 0);
            (NoteList.ContainerFromIndex(NoteList.SelectedIndex) as Control)?.Focus();
            e.Handled = true;
        }
    }

    private void TitleKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Down)
        {
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void NameKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Notes?.NameOkCommand.Execute(null);
        else if (e.Key == Key.Escape)
            Notes?.NameCancelCommand.Execute(null);
        else
            return;
        e.Handled = true;
    }

    private void EditorKeys(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.K)
        {
            LinkClicked(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.C)
        {
            Editor.ToggleList(ListKind.Check);
            e.Handled = true;
        }
    }

    // The format bar, as in Mail

    /// <summary>The bar shows what is on at the caret: bold, the list, the style.</summary>
    private void ShowFormat()
    {
        showingFormat = true;
        var f = Editor.CurrentFormat;
        var paragraph = Editor.CurrentParagraph;
        BoldButton.Classes.Set("on", f.Bold);
        ItalicButton.Classes.Set("on", f.Italic);
        UnderlineButton.Classes.Set("on", f.Underline);
        ChecklistButton.Classes.Set("on", paragraph?.List == ListKind.Check);
        BulletsButton.Classes.Set("on", paragraph?.List == ListKind.Bullet);
        NumbersButton.Classes.Set("on", paragraph?.List == ListKind.Number);
        StyleBox.SelectedIndex = (int)(paragraph?.Style ?? BlockStyle.Paragraph);
        showingFormat = false;
    }

    private void Format(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string action })
            return;
        switch (action)
        {
            case "bold": Editor.ToggleBold(); break;
            case "italic": Editor.ToggleItalic(); break;
            case "underline": Editor.ToggleUnderline(); break;
            case "clear": Editor.ClearFormatting(); break;
            case "checklist": Editor.ToggleList(ListKind.Check); break;
            case "bullets": Editor.ToggleList(ListKind.Bullet); break;
            case "numbers": Editor.ToggleList(ListKind.Number); break;
            case "indent": Editor.Indent(1); break;
            case "outdent": Editor.Indent(-1); break;
            case var h when h.StartsWith("highlight:", StringComparison.Ordinal): Editor.SetHighlight(h.Length > 10 ? h[10..] : null); break;
        }
        (sender as Control)?.FindLogicalAncestorOfType<Popup>()?.Close();
        ShowFormat();
    }

    private void StyleChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (!showingFormat && StyleBox.SelectedItem is ComboBoxItem { Tag: string style } && Enum.TryParse<BlockStyle>(style, out var chosen))
            Editor.SetStyle(chosen);
    }

    private void InsertRule(object? sender, RoutedEventArgs e) => Editor.InsertRule();

    private void InsertTable(object? sender, RoutedEventArgs e)
    {
        switch ((sender as MenuItem)?.Tag)
        {
            case "row": Editor.AddTableRow(); break;
            case "column": Editor.AddTableColumn(); break;
            case string size when size.Split('x') is [var rows, var columns]:
                Editor.InsertTable(int.Parse(rows, CultureInfo.InvariantCulture), int.Parse(columns, CultureInfo.InvariantCulture));
                break;
        }
    }

    private void InsertPicture(object? sender, RoutedEventArgs e) => InsertPictureAsync().Forget("Putting a picture in the note", tell: true);

    private async Task InsertPictureAsync()
    {
        if ((await Picker.Files(this, "Put a picture in the note", ["*.png", "*.jpg", "*.jpeg", "*.gif"], kindName: "Pictures")).FirstOrDefault() is not { } path)
            return;
        var bytes = await File.ReadAllBytesAsync(path);
        if (ImageSize.Of(bytes) is not { } size)
            return;
        Editor.InsertPicture(bytes, size.ContentType, size.Width, size.Height);
    }

    // Links: the editor keeps its selection while the link box has the focus, so the link goes on the words picked.

    private void LinkClicked(object? sender, RoutedEventArgs e)
    {
        LinkBox.Text = Editor.CurrentFormat.Link ?? "";
        LinkBar.IsVisible = true;
        Dispatcher.UIThread.Post(() => LinkBox.Focus());
    }

    private void AddLinkClicked(object? sender, RoutedEventArgs e) => AddLink();

    private void CancelLinkClicked(object? sender, RoutedEventArgs e) => CancelLink();

    private void RemoveLinkClicked(object? sender, RoutedEventArgs e)
    {
        Editor.RemoveLink();
        CancelLink();
    }

    private void AddLink()
    {
        if (!string.IsNullOrWhiteSpace(LinkBox.Text))
            Editor.SetLink(LinkBox.Text);
        CancelLink();
    }

    private void CancelLink()
    {
        LinkBar.IsVisible = false;
        LinkBox.Text = "";
        Editor.Focus();
    }
}
