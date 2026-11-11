namespace Webignite.Core.Chat;

/// <summary>What a person shows as in the chat. Offline is never picked: it's worked out when a PC goes quiet.</summary>
public enum PresenceState { Available, Busy, Away, Offline }

/// <summary>
/// A person's status as it travels between the PCs: what they picked, when their PC last synced, and when they last
/// used the PC (so they show Away after a while without using it).
/// </summary>
public sealed record Presence(string PersonId, PresenceState Chosen, DateTimeOffset LastSeen, DateTimeOffset LastActive);

public static class PresenceRules
{
    /// <summary>Without using the PC this long, someone Available shows Away.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(10);

    /// <summary>A PC that hasn't synced for this long is taken to be off (it syncs every minute while Webignite is open).</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(3);

    /// <summary>The statuses a person can pick.</summary>
    public static IReadOnlyList<PresenceState> Choices { get; } = [PresenceState.Available, PresenceState.Busy, PresenceState.Away];

    /// <summary>What someone shows as now.</summary>
    public static PresenceState Shown(Presence? presence, DateTimeOffset now)
    {
        if (presence is null || now - presence.LastSeen > OfflineAfter)
            return PresenceState.Offline;
        if (presence.Chosen != PresenceState.Available)
            return presence.Chosen;
        return now - presence.LastActive > IdleAfter ? PresenceState.Away : PresenceState.Available;
    }

    public static string Word(PresenceState state) => state switch
    {
        PresenceState.Available => "Available",
        PresenceState.Busy => "Busy",
        PresenceState.Away => "Away",
        _ => "Offline",
    };

    /// <summary>A pop-up and a sound for a new message, unless I'm busy (then only the count).</summary>
    public static bool Interrupts(PresenceState mine) => mine != PresenceState.Busy;
}
