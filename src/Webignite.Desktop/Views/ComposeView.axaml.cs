using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ComposeView : UserControl
{
    public ComposeView()
    {
        InitializeComponent();
        Watch(ToBox, AddressField.To);
        Watch(CcBox, AddressField.Cc);
        Watch(BccBox, AddressField.Bcc);
        BodyBox.AddHandler(KeyDownEvent, BodyKeys, RoutingStrategies.Tunnel);
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
    }

    // The selection is kept here while the link box has the focus.
    private (int Start, int End) linkSelection;

    private void BodyKeys(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;
        switch (e.Key)
        {
            case Key.B: ApplyFormat("bold"); e.Handled = true; break;
            case Key.I: ApplyFormat("italic"); e.Handled = true; break;
        }
    }

    private void FormatClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action })
            ApplyFormat(action);
    }

    private void ApplyFormat(string action) => ApplyFormat(action, BodyBox.SelectionStart, BodyBox.SelectionEnd);

    private void ApplyFormat(string action, int start, int end)
    {
        if (Compose is not { } compose)
            return;
        var edit = compose.Format(action, start, end);
        BodyBox.Text = edit.Text;
        BodyBox.Focus();
        BodyBox.SelectionStart = edit.SelectionStart;
        BodyBox.SelectionEnd = edit.SelectionEnd;
        BodyBox.CaretIndex = edit.SelectionEnd;
    }

    /// <summary>Fills the Saved replies menu: each reply, then save the selected text, then remove one.</summary>
    private void RepliesOpening(object? sender, EventArgs e)
    {
        if (Compose is not { } compose || sender is not MenuFlyout flyout)
            return;
        var caret = BodyBox.CaretIndex;
        var (start, end) = (Math.Min(BodyBox.SelectionStart, BodyBox.SelectionEnd), Math.Max(BodyBox.SelectionStart, BodyBox.SelectionEnd));
        var selected = end > start ? compose.Body[start..Math.Min(end, compose.Body.Length)] : "";
        flyout.Items.Clear();
        foreach (var reply in compose.SavedReplyList)
        {
            var item = new MenuItem { Header = reply.Name };
            ToolTip.SetTip(item, compose.Fill(reply));
            item.Click += (_, _) =>
            {
                var after = compose.Insert(reply, caret);
                BodyBox.Text = compose.Body;
                BodyBox.Focus();
                BodyBox.CaretIndex = after;
            };
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

    private void LinkClicked(object? sender, RoutedEventArgs e)
    {
        if (Compose is not { } compose)
            return;
        linkSelection = (BodyBox.SelectionStart, BodyBox.SelectionEnd);
        compose.AskingForLink = true;
        Dispatcher.UIThread.Post(() => LinkBox.Focus());
    }

    private void AddLinkClicked(object? sender, RoutedEventArgs e) => AddLink();

    private void CancelLinkClicked(object? sender, RoutedEventArgs e) => CancelLink();

    private void AddLink()
    {
        if (Compose is not { } compose)
            return;
        if (string.IsNullOrWhiteSpace(compose.LinkAddress))
        {
            CancelLink();
            return;
        }
        ApplyFormat("link", linkSelection.Start, linkSelection.End);
    }

    private void CancelLink()
    {
        if (Compose is not { } compose)
            return;
        compose.AskingForLink = false;
        compose.LinkAddress = "";
        BodyBox.Focus();
    }

    private ComposeViewModel? Compose => DataContext as ComposeViewModel;

    // Suggest names and addresses only while the person is typing in the box, not when it's filled in for them.
    private void Watch(TextBox box, AddressField field)
    {
        box.TextChanged += async (_, _) =>
        {
            if (box.IsFocused && Compose is { } compose)
            {
                SuggestionsPopup.PlacementTarget = box;
                await compose.SuggestAsync(field, box.Text);
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
                case Key.Enter or Key.Tab: Accept(box); e.Handled = true; break;
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

    private void Accept(TextBox box, Webignite.Core.Mail.Contact? contact = null)
    {
        if (Compose?.AcceptSuggestion(contact) is { } text)
        {
            box.Focus();
            box.CaretIndex = text.Length;
        }
    }

    private void SuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (SuggestionsPopup.PlacementTarget is TextBox box && SuggestionList.SelectedItem is Webignite.Core.Mail.Contact contact)
            Accept(box, contact);
    }

    private async void OnAttach(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ComposeViewModel compose || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Attach files", AllowMultiple = true });
        compose.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
}
