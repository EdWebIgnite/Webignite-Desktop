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
