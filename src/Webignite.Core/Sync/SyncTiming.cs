namespace Webignite.Core.Sync;

/// <summary>
/// How often sync runs. Every minute it takes in what your partner sent and sends chat and your status, so messages
/// stay quick; everything else (projects, clients, quotes and invoices, settings) goes every 15 minutes, when you
/// click Sync now, and as Webignite closes.
/// </summary>
public static class SyncTiming
{
    public static readonly TimeSpan FullEvery = TimeSpan.FromMinutes(15);

    /// <summary>Whether this sync should send everything: the first one after starting, then every 15 minutes.</summary>
    public static bool FullDue(DateTimeOffset? lastFull, DateTimeOffset now) => lastFull is not { } last || now - last >= FullEvery;
}
