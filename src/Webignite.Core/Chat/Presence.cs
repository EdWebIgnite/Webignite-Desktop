namespace Webignite.Core.Chat;

/// <summary>What a person shows as in the chat. Offline is never picked: it's worked out when a PC goes quiet.</summary>
public enum PresenceState { Available, Busy, Away, Offline }

/// <summary>
/// A person's status as it travels between the PCs: what they picked, when their PC last synced, when they last
/// used the PC (so they show Away after a while without using it), and how far they've read the chat (for the
/// two ticks on what I sent). PCs on an older Webignite send no ReadUpTo.
/// </summary>
public sealed record Presence(string PersonId, PresenceState Chosen, DateTimeOffset LastSeen, DateTimeOffset LastActive, DateTimeOffset? ReadUpTo = null);

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

    /// <summary>
    /// The line under my partner's name, as WhatsApp's: "online", "away", "busy", or "last seen today at 09:01"
    /// once their PC has gone quiet; "offline" when nothing has been heard from it.
    /// </summary>
    public static string Line(Presence? presence, DateTimeOffset now, TimeZoneInfo zone, IFormatProvider culture) => Shown(presence, now) switch
    {
        PresenceState.Available => "online",
        PresenceState.Busy => "busy",
        PresenceState.Away => "away",
        _ when presence is null => "offline",
        _ => "last seen " + Lower(ChatRules.When(presence.LastSeen, now, zone, culture)),
    };

    // "Today at 09:01" reads "today at 09:01" after "last seen"; a weekday or date keeps its capital.
    private static string Lower(string said) =>
        said.StartsWith("Today", StringComparison.Ordinal) || said.StartsWith("Yesterday", StringComparison.Ordinal) ? char.ToLowerInvariant(said[0]) + said[1..] : said;

    /// <summary>A pop-up and a sound for a new message, unless I'm busy (then only the count).</summary>
    public static bool Interrupts(PresenceState mine) => mine != PresenceState.Busy;
}
