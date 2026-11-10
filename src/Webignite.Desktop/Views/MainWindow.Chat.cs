using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The chat with your partner, in a window of its own; it closes with the app.</summary>
public partial class MainWindow
{
    private ChatWindow? chatWindow;

    private void WireChat(AppServices services, MainViewModel main)
    {
        services.Go.OpenChatAction = () => ShowChat(services);
        services.ChatArrived = () =>
        {
            chatWindow?.Arrived();
            main.UpdateChatUnread();
        };
        services.ChatRead = main.UpdateChatUnread;
        main.UpdateChatUnread();
        Closing += (_, _) => chatWindow?.Close();
    }

    /// <summary>Opens the chat, or brings it forward. Returns the window, for the screenshot tool.</summary>
    public ChatWindow ShowChat(AppServices services)
    {
        if (chatWindow is { } open)
        {
            open.Activate();
            return open;
        }
        var window = new ChatWindow(new ChatViewModel(services));
        window.Closed += (_, _) => chatWindow = null;
        chatWindow = window;
        window.Show(this);
        return window;
    }

    /// <summary>New messages came in with a sync: shown in the open chat.</summary>
    public void ChatArrived() => chatWindow?.Arrived();
}
