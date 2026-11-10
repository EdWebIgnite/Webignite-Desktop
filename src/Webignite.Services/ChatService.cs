using Webignite.Core.Chat;
using Webignite.Core.Projects;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Chat between the two of you: sending messages with files, reading them back, and how far this PC has read.
/// Messages go to the other PC with the next sync, locked like everything else.
/// </summary>
public sealed class ChatService(LocalStore store, TimeProvider? clock = null)
{
    private const string ReadName = "chat-read";
    private const string AnnouncedName = "chat-announced";
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    private TeamSettings Team => store.GetSetting<TeamSettings>("team") ?? new TeamSettings();

    public string? MyId => Team.Me?.Id;

    /// <summary>The name to show for a writer: "You", or the partner's name.</summary>
    public string NameOf(string personId) => personId == MyId ? "You" : Team.Partner is { } partner && partner.Id == personId ? partner.Name : "Your partner";

    public string PartnerName => Team.Partner?.Name ?? "your partner";

    public IReadOnlyList<ChatMessage> Messages() => store.ListChatMessages();

    /// <summary>Sends a message with any files (each at most 10 MB); returns it, or null with nothing to send.</summary>
    public ChatMessage? Send(string text, IReadOnlyList<(string Name, byte[] Data)> files)
    {
        if (MyId is not { } me || (string.IsNullOrWhiteSpace(text) && files.Count == 0))
            return null;
        var sent = new List<ChatFile>();
        foreach (var (name, data) in files)
        {
            if (ChatRules.Problem(name, data.Length) is { } problem)
                throw new InvalidOperationException(problem);
            var (file, parts) = ChatRules.Split(name, data);
            foreach (var part in parts)
                store.SaveChatPart(part);
            sent.Add(file);
        }
        var message = store.SaveChatMessage(new ChatMessage { Id = ChatMessage.NewId(), From = me, Text = text.Trim(), Files = sent, Sent = clock.GetUtcNow() });
        MarkRead();
        return message;
    }

    /// <summary>A file's bytes, or null while parts are still on their way.</summary>
    public byte[]? FileBytes(ChatFile file) => ChatRules.Join(file, store.ListChatParts(file.Id));

    public int Unread => MyId is { } me ? ChatRules.Unread(store.ListChatMessages(), me, ReadUpTo) : 0;

    private DateTimeOffset ReadUpTo => store.GetSetting<DateTimeOffset?>(ReadName) ?? DateTimeOffset.MinValue;

    /// <summary>Everything here has been seen (the chat window is open and in front).</summary>
    public void MarkRead()
    {
        var latest = store.ListChatMessages(1).LastOrDefault()?.Sent ?? clock.GetUtcNow();
        if (latest > ReadUpTo)
            store.SetSetting(ReadName, latest);
    }

    /// <summary>The partner's messages that arrived since this was last asked, for a note each; only the newest few.</summary>
    public IReadOnlyList<ChatMessage> NewlyArrived()
    {
        if (MyId is not { } me)
            return [];
        var since = store.GetSetting<DateTimeOffset?>(AnnouncedName) ?? ReadUpTo;
        var fresh = store.ListChatMessages(50).Where(m => m.From != me && m.Sent > since && m.Sent > ReadUpTo).ToList();
        if (fresh.Count > 0)
            store.SetSetting(AnnouncedName, fresh[^1].Sent);
        return fresh;
    }
}
