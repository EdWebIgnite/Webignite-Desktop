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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Notes is { } notes && this.IsAttachedToVisualTree())
            notes.FocusTitle = FocusTitle;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Put a picture in the note",
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
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
