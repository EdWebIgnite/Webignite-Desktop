using System.Globalization;
using Webignite.Core.Sync;

namespace Webignite.Tests;

/// <summary>A PC whose sync has stopped says so, once it has lasted long enough not to be a blip.</summary>
public sealed class SyncTroubleTests
{
    private static readonly DateTimeOffset Began = new(2026, 11, 20, 16, 49, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Sast = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");

    [Fact]
    public void A_failed_sync_starts_the_trouble_and_one_that_gets_through_ends_it()
    {
        Assert.Null(SyncTroubles.After(null, null, Began));

        var started = SyncTroubles.After(null, "Sign in again.", Began);
        Assert.Equal(new SyncTrouble("Sign in again.", Began), started);

        // It goes on: when it began is kept, and what is wrong now is what is said.
        var later = SyncTroubles.After(started, "Couldn't reach the sync server.", Began.AddMinutes(5));
        Assert.Equal(new SyncTrouble("Couldn't reach the sync server.", Began), later);

        Assert.Null(SyncTroubles.After(later, null, Began.AddMinutes(6)));
    }

    [Fact]
    public void It_is_said_only_once_it_has_lasted_ten_minutes()
    {
        var trouble = new SyncTrouble("Sign in again.", Began);
        Assert.False(SyncTroubles.IsWorthSaying(null, Began.AddDays(1)));
        Assert.False(SyncTroubles.IsWorthSaying(trouble, Began.AddMinutes(3))); // a laptop waking up, the internet dropping
        Assert.True(SyncTroubles.IsWorthSaying(trouble, Began.AddMinutes(10)));
        Assert.True(SyncTroubles.IsWorthSaying(trouble, Began.AddDays(5)));
    }

    [Theory]
    [InlineData("Sign in again.", "Chat and shared projects stopped syncing on this PC at 18:49 on 20 Nov. Sign in again.")]
    [InlineData("Couldn't reach the sync server", "Chat and shared projects stopped syncing on this PC at 18:49 on 20 Nov. Couldn't reach the sync server.")]
    [InlineData(SyncTroubles.SignedOut, "Chat and shared projects stopped syncing on this PC at 18:49 on 20 Nov. You're signed out on this PC.")]
    public void The_line_says_since_when_in_the_PCs_own_time_and_what_is_wrong(string problem, string line) =>
        Assert.Equal(line, SyncTroubles.Line(new SyncTrouble(problem, Began), Sast, CultureInfo.GetCultureInfo("en-ZA")));
}
