using Avalonia.Controls;
using Avalonia.Media;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The chat with your partner, in a small window of its own beside whatever page is open.</summary>
public sealed class ChatWindow : Window
{
    private readonly ChatViewModel chat;
    private readonly ChatView view;

    public ChatWindow(ChatViewModel chat)
    {
        this.chat = chat;
        DataContext = chat;
        Title = chat.Title;
        // The name can arrive after the window opened (from the workspace or your partner's PC).
        chat.PropertyChanged += TitleChanged;
        Width = 460;
        Height = 620;
        MinWidth = 340;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = this.TryFindResource("Canvas", out var canvas) && canvas is IBrush brush ? brush : Brushes.WhiteSmoke;
        Content = view = new ChatView();
        chat.Refresh();
        Opened += (_, _) =>
        {
            view.ScrollToEnd();
            view.FocusDraft();
            chat.Seen();
        };
        Activated += (_, _) => chat.Seen();
        Closed += (_, _) =>
        {
            chat.PropertyChanged -= TitleChanged;
            chat.Dispose();
        };
    }

    private void TitleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.Title))
            Title = chat.Title;
    }

    public ChatViewModel Chat => chat;

    /// <summary>New messages arrived: shown, and read if the window is in front.</summary>
    public void Arrived()
    {
        chat.Refresh();
        view.ScrollToEnd();
        if (IsActive)
            chat.Seen();
    }
}
