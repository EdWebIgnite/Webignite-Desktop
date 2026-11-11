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
        services.ChatPopUp = fresh => PopUpChat(services, fresh);
        services.PresenceChanged = () =>
        {
            chatWindow?.Chat.ShowPresence();
            main.UpdateChatPresence();
        };
        main.UpdateChatUnread();
        main.UpdateChatPresence();
        Closing += (_, _) =>
        {
            chatWindow?.Close();
            chatPopUp?.Close();
        };
    }

    private ChatPopUp? chatPopUp;

    // A new message while the chat isn't in front: a pop-up in the corner of the screen, a short sound and the taskbar
    // flashing, unless I'm Busy (then only the count on the Chat button).
    private void PopUpChat(AppServices services, IReadOnlyList<Webignite.Core.Chat.ChatMessage> fresh)
    {
        if (chatWindow is { IsActive: true } || !Webignite.Core.Chat.PresenceRules.Interrupts(services.Chat.MyShown))
            return;
        var last = fresh[^1];
        var from = services.Chat.NameOf(last.From);
        var text = Webignite.Core.Chat.ChatRules.Preview(last) + (fresh.Count > 1 ? $" (and {fresh.Count - 1} more)" : "");
        if (chatPopUp is null)
        {
            chatPopUp = new ChatPopUp(() => ShowChat(services));
            chatPopUp.Closed += (_, _) => chatPopUp = null;
        }
        chatPopUp.Say(from, text);
        IdleClock.Chime();
        if (!IsActive)
            Flash();
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
