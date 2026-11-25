namespace Webignite.Core.Sync;

/// <summary>Syncing isn't working on this PC: what the server or the app said, and since when.</summary>
public sealed record SyncTrouble(string Problem, DateTimeOffset Since);

/// <summary>
/// When to tell the person that sync has stopped. A PC that can't sync looks fine from the inside: its own chat
/// and projects are all there. What stops is everything to and from the other PC, and the other person sees this
/// one as "last seen" days ago. So it is said on the PC it happens on, once it has lasted long enough not to be a
/// laptop waking up or the internet dropping for a minute.
/// </summary>
public static class SyncTroubles
{
    /// <summary>How long sync must have been failing before the person is told.</summary>
    public static readonly TimeSpan SaidAfter = TimeSpan.FromMinutes(10);

    /// <summary>What a PC that synced before and is now signed out says.</summary>
    public const string SignedOut = "You're signed out on this PC.";

    public static bool IsWorthSaying(SyncTrouble? trouble, DateTimeOffset now) => trouble is not null && now - trouble.Since >= SaidAfter;

    /// <summary>
    /// The trouble as it stands after a sync: none once one gets through, the same one (and when it began) while
    /// it goes on, a new one from now.
    /// </summary>
    public static SyncTrouble? After(SyncTrouble? before, string? problem, DateTimeOffset now) =>
        problem is null ? null : before is null ? new SyncTrouble(problem, now) : before with { Problem = problem };

    /// <summary>One line for the person: "Chat and shared projects stopped syncing on this PC at 18:49 on 20 Nov. Sign in again."</summary>
    public static string Line(SyncTrouble trouble, TimeZoneInfo zone, IFormatProvider culture)
    {
        var since = TimeZoneInfo.ConvertTime(trouble.Since, zone);
        var problem = trouble.Problem.Trim();
        if (problem.Length > 0 && !".!?".Contains(problem[^1]))
            problem += ".";
        return string.Create(culture, $"Chat and shared projects stopped syncing on this PC at {since:t} on {since:d MMM}. {problem}");
    }
}
