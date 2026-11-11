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

    // Status: what I picked (this PC only) and what the others' PCs last said (kept as they arrive).
    private const string ChosenName = "presence-mine";
    private const string SeenName = "presence-seen";

    /// <summary>When this person last used the PC; set by the app (Windows says how long since the last key or click).</summary>
    public Func<DateTimeOffset> LastActive { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>The status I picked: Available, Busy or Away.</summary>
    public PresenceState Chosen
    {
        get => store.GetSetting<PresenceState?>(ChosenName) ?? PresenceState.Available;
        set => store.SetSetting(ChosenName, value == PresenceState.Offline ? PresenceState.Available : value);
    }

    /// <summary>My status as it goes to the other PC with this sync; null when the team isn't set up yet.</summary>
    public Presence? Mine() => MyId is { } me ? new Presence(me, Chosen, clock.GetUtcNow(), LastActive()) : null;

    /// <summary>What I show as now (Away after a while without using the PC, unless I picked otherwise).</summary>
    public PresenceState MyShown => PresenceRules.Shown(Mine(), clock.GetUtcNow());

    /// <summary>What my partner shows as now: Offline when their PC hasn't synced for a few minutes.</summary>
    public PresenceState PartnerShown =>
        PresenceRules.Shown(Team.Partner is { } partner ? Seen().GetValueOrDefault(partner.Id) : null, clock.GetUtcNow());

    /// <summary>A status from another PC, kept unless it's older than the one already here.</summary>
    public void TakeIn(Presence presence)
    {
        if (presence.PersonId == MyId)
            return;
        var seen = Seen();
        if (seen.TryGetValue(presence.PersonId, out var kept) && kept.LastSeen >= presence.LastSeen)
            return;
        seen[presence.PersonId] = presence;
        store.SetSetting(SeenName, seen);
    }

    private Dictionary<string, Presence> Seen() => store.GetSetting<Dictionary<string, Presence>>(SeenName) ?? [];

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
