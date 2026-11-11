using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Webignite.Core.Writing;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

using Webignite.Desktop.Files;

namespace Webignite.Desktop.Views;

public partial class ComposeView : UserControl
{
    public ComposeView()
    {
        InitializeComponent();
        Looks.Attach(this, "Compose");
        // The quoted mail never fetches anything from the internet.
        QuotePanel.ImageLoad += (_, e) =>
        {
            if (!Webignite.Core.Mail.MailImages.Allowed(e.Event.Src, remoteAllowed: false))
                e.Event.Handled = true;
        };
        QuotePanel.StylesheetLoad += (_, e) =>
        {
            if (!Webignite.Core.Mail.MailImages.Allowed(e.Event.Src, remoteAllowed: false))
                e.Event.SetStyleSheet = "/* not loaded */";
        };
        Watch(ToBox, AddressField.To);
        Watch(CcBox, AddressField.Cc);
        Watch(BccBox, AddressField.Bcc);
        Watch(ReplyToBox, AddressField.ReplyTo);
        LinkBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) { AddLink(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelLink(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        Root.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        Root.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (Compose is { } compose && e.DataTransfer.TryGetFiles() is { } files)
                compose.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        });
        Editor.SelectionChanged += (_, _) => ShowFormat();
        Editor.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                LinkClicked(null, new RoutedEventArgs());
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        foreach (var size in ComposeViewModel.Sizes)
            SizeBox.Items.Add(new ComboBoxItem { Content = TextSizes.Label(size), Tag = size });
        Swatches(TextColourList, ComposeViewModel.TextColours, "colour:");
        Swatches(HighlightList, ComposeViewModel.HighlightColours, "highlight:");
        foreach (var emoji in Emoji.Common)
        {
            var button = new Button { Classes = { "quiet", "format" }, Content = emoji, Focusable = false, FontSize = 18, Padding = new Thickness(4, 0), Tag = "emoji:" + emoji };
            button.Click += Format;
            EmojiList.Items.Add(button);
        }
        ShowFormat();
    }

    // Spell check comes on with the editor, so Windows' checker is only opened once a mail is written.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Editor.Spelling = App.Services?.Spelling;
    }

    // Each colour is its own button; the colours are what the mail will carry, not the app's own look.
    private void Swatches(ItemsControl list, IReadOnlyList<string> colours, string action)
    {
        foreach (var colour in colours)
        {
            var swatch = new Button
            {
                Classes = { "shade" },
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(0),
                Focusable = false,
                Tag = action + colour,
                Content = new Border { Background = Brush.Parse(colour), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Width = 20, Height = 20 },
            };
            ToolTip.SetTip(swatch, MailColours.Name(colour));
            swatch.Click += Format;
            list.Items.Add(swatch);
        }
    }

    private bool showingFormat;

    /// <summary>The bar shows what is on at the caret: bold, the list, the style, the size.</summary>
    private void ShowFormat()
    {
        showingFormat = true;
        var f = Editor.CurrentFormat;
        var paragraph = Editor.CurrentParagraph;
        BoldButton.Classes.Set("on", f.Bold);
        ItalicButton.Classes.Set("on", f.Italic);
        UnderlineButton.Classes.Set("on", f.Underline);
        BulletsButton.Classes.Set("on", paragraph?.List == ListKind.Bullet);
        NumbersButton.Classes.Set("on", paragraph?.List == ListKind.Number);
        var align = paragraph?.Align ?? Alignment.Left;
        LeftButton.Classes.Set("on", paragraph is not null && align == Alignment.Left);
        CentreButton.Classes.Set("on", align == Alignment.Centre);
        RightButton.Classes.Set("on", align == Alignment.Right);
        StyleBox.SelectedIndex = (int)(paragraph?.Style ?? BlockStyle.Paragraph);
        FontBox.SelectedIndex = f.Fixed ? 1 : 0;
        SizeBox.SelectedIndex = TextSizes.All.ToList().IndexOf(f.Size);
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
            case "smaller": Editor.StepSize(-1); break;
            case "larger": Editor.StepSize(1); break;
            case "bullets": Editor.ToggleList(ListKind.Bullet); break;
            case "numbers": Editor.ToggleList(ListKind.Number); break;
            case "indent": Editor.Indent(1); break;
            case "outdent": Editor.Indent(-1); break;
            case "left": Editor.Align(Alignment.Left); break;
            case "centre": Editor.Align(Alignment.Centre); break;
            case "right": Editor.Align(Alignment.Right); break;
            case var c when c.StartsWith("colour:", StringComparison.Ordinal): Editor.SetColour(c.Length > 7 ? c[7..] : null); break;
            case var h when h.StartsWith("highlight:", StringComparison.Ordinal): Editor.SetHighlight(h.Length > 10 ? h[10..] : null); break;
            case var m when m.StartsWith("emoji:", StringComparison.Ordinal): Editor.InsertText(m[6..]); break;
        }
        (sender as Control)?.FindLogicalAncestorOfType<Popup>()?.Close();
        ShowFormat();
    }

    private void StyleChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (!showingFormat && StyleBox.SelectedItem is ComboBoxItem { Tag: string style } && Enum.TryParse<BlockStyle>(style, out var chosen))
            Editor.SetStyle(chosen);
    }

    private void FontChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (!showingFormat && FontBox.SelectedIndex >= 0)
            Editor.SetFixed(FontBox.SelectedIndex == 1);
    }

    private void SizeChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (!showingFormat && SizeBox.SelectedItem is ComboBoxItem { Tag: TextSize size })
            Editor.SetSize(size);
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

    private void InsertPicture(object? sender, RoutedEventArgs e) => InsertPictureAsync().Forget("Putting a picture in the mail", tell: true);

    private async Task InsertPictureAsync()
    {
        if ((await Picker.Files(this, "Put a picture in the mail", ["*.png", "*.jpg", "*.jpeg", "*.gif"], kindName: "Pictures")).FirstOrDefault() is not { } path)
            return;
        var bytes = await File.ReadAllBytesAsync(path);
        if (ImageSize.Of(bytes) is not { } size)
        {
            Compose?.Tell("That file isn't a PNG, JPG or GIF picture.");
            return;
        }
        Editor.InsertPicture(bytes, size.ContentType, size.Width, size.Height);
    }

    /// <summary>Fills the Send later menu: the quick times, then pick a day and hour.</summary>
    private void SendLaterOpening(object? sender, EventArgs e)
    {
        if (Compose is not { } compose || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        foreach (var (label, when) in compose.SendLaterChoices())
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => compose.SendAt(when).Forget("Setting a mail to send later");
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        var pick = new MenuItem { Header = "Pick a day and time…" };
        pick.Click += (_, _) => compose.StartPickSendTime();
        flyout.Items.Add(pick);
    }

    /// <summary>Fills the Saved replies menu: each reply, then save the selected text, then remove one.</summary>
    private void RepliesOpening(object? sender, EventArgs e)
    {
        if (Compose is not { } compose || sender is not MenuFlyout flyout)
            return;
        var selected = Editor.SelectedText;
        flyout.Items.Clear();
        foreach (var reply in compose.SavedReplyList)
        {
            var item = new MenuItem { Header = reply.Name };
            ToolTip.SetTip(item, compose.Fill(reply));
            item.Click += (_, _) => Editor.InsertDocument(compose.Writing(reply));
            flyout.Items.Add(item);
        }
        if (compose.SavedReplyList.Count == 0)
            flyout.Items.Add(new MenuItem { Header = "No saved replies yet", IsEnabled = false });
        flyout.Items.Add(new Separator());
        var save = new MenuItem { Header = selected.Length > 0 ? "Save the selected text as a reply…" : "Select some text to save it as a reply", IsEnabled = selected.Length > 0 };
        save.Click += (_, _) =>
        {
            compose.StartSaveReply(selected);
            Dispatcher.UIThread.Post(() => ReplyNameBox.Focus());
        };
        flyout.Items.Add(save);
        if (compose.SavedReplyList.Count > 0)
        {
            var remove = new MenuItem { Header = "Remove a saved reply" };
            foreach (var reply in compose.SavedReplyList)
            {
                var item = new MenuItem { Header = reply.Name };
                item.Click += (_, _) => compose.RemoveReply(reply);
                remove.Items.Add(item);
            }
            flyout.Items.Add(remove);
        }
    }

    /// <summary>Fills the Templates menu: each template, then keep this mail as one, then remove one.</summary>
    private void TemplatesOpening(object? sender, EventArgs e)
    {
        if (Compose is not { } compose || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        foreach (var template in compose.TemplateList)
        {
            var item = new MenuItem { Header = template.Name };
            if (template.Subject.Length > 0)
                ToolTip.SetTip(item, template.Subject);
            item.Click += (_, _) =>
            {
                if (compose.UseTemplate(template) is { } writing)
                    Editor.InsertDocument(writing);
            };
            flyout.Items.Add(item);
        }
        if (compose.TemplateList.Count == 0)
            flyout.Items.Add(new MenuItem { Header = "No templates yet", IsEnabled = false });
        flyout.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save this mail as a template…" };
        save.Click += (_, _) =>
        {
            compose.StartSaveTemplateCommand.Execute(null);
            Dispatcher.UIThread.Post(() => TemplateNameBox.Focus());
        };
        flyout.Items.Add(save);
        if (compose.TemplateList.Count > 0)
        {
            var remove = new MenuItem { Header = "Remove a template" };
            foreach (var template in compose.TemplateList)
            {
                var item = new MenuItem { Header = template.Name };
                item.Click += (_, _) => compose.RemoveTemplate(template);
                remove.Items.Add(item);
            }
            flyout.Items.Add(remove);
        }
    }

    private void PeopleOpening(object? sender, EventArgs e)
    {
        Compose?.OpenPeople();
        Dispatcher.UIThread.Post(() => PeopleSearchBox.Focus());
    }

    private void LinkClicked(object? sender, RoutedEventArgs e)
    {
        if (Compose is not { } compose)
            return;
        compose.LinkAddress = Editor.CurrentFormat.Link ?? "";
        compose.AskingForLink = true;
        Dispatcher.UIThread.Post(() => LinkBox.Focus());
    }

    private void AddLinkClicked(object? sender, RoutedEventArgs e) => AddLink();

    private void CancelLinkClicked(object? sender, RoutedEventArgs e) => CancelLink();

    private void RemoveLinkClicked(object? sender, RoutedEventArgs e)
    {
        Editor.RemoveLink();
        CancelLink();
    }

    // The editor keeps its selection while the link box has the focus, so the link goes on the words picked.
    private void AddLink()
    {
        if (Compose is not { } compose)
            return;
        if (!string.IsNullOrWhiteSpace(compose.LinkAddress))
            Editor.SetLink(compose.LinkAddress);
        CancelLink();
    }

    private void CancelLink()
    {
        if (Compose is not { } compose)
            return;
        compose.AskingForLink = false;
        compose.LinkAddress = "";
        Editor.Focus();
    }

    private ComposeViewModel? Compose => DataContext as ComposeViewModel;

    // Suggest names and addresses only while the person is typing in the box, not when it's filled in for them.
    private void Watch(AddressBox address, AddressField field)
    {
        var box = address.Box;
        box.TextChanged += (_, _) =>
        {
            if (box.IsFocused && Compose is { } compose)
            {
                SuggestionsPopup.PlacementTarget = address;
                // Suggestions are a help, not a must: a failed lookup is logged, not shown while typing.
                compose.SuggestAsync(field, address.Text).Forget("Suggesting addresses");
            }
        };
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (Compose is not { HasSuggestions: true } compose)
                return;
            switch (e.Key)
            {
                case Key.Down: compose.MoveSuggestion(1); e.Handled = true; break;
                case Key.Up: compose.MoveSuggestion(-1); e.Handled = true; break;
                case Key.Enter or Key.Tab: Accept(address); e.Handled = true; break;
                case Key.Escape: compose.CloseSuggestions(); e.Handled = true; break;
            }
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // Leave the list open when the click went to it.
            if (!SuggestionList.IsKeyboardFocusWithin && !SuggestionList.IsPointerOver)
                Compose?.CloseSuggestions();
        });
    }

    // The picked person becomes a chip; the cursor goes back to the box for the next one.
    private void Accept(AddressBox address, Webignite.Core.Mail.Contact? contact = null)
    {
        if (Compose?.AcceptSuggestion(contact) is not null)
        {
            address.Box.Focus();
            address.Box.CaretIndex = address.Box.Text?.Length ?? 0;
        }
    }

    private void SuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (SuggestionsPopup.PlacementTarget is AddressBox address && SuggestionList.SelectedItem is Webignite.Core.Mail.Contact contact)
            Accept(address, contact);
    }

    private void OnAttach(object? sender, RoutedEventArgs e) => OnAttachAsync(sender, e).Forget("Attaching files", tell: true);

    private async Task OnAttachAsync(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ComposeViewModel compose)
            return;
        compose.AddFiles(await Picker.Files(this, "Attach files", many: true));
    }
}
