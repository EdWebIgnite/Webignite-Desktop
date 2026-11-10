using Webignite.Core.Sync;

namespace Webignite.Tests;

/// <summary>Which settings both PCs share, which copy wins, and which record kinds the server takes.</summary>
public sealed class SharedSettingsTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_business_settings_are_shared_but_mail_looks_and_layout_are_not()
    {
        Assert.True(SharedSettings.IsShared("documents"));
        Assert.True(SharedSettings.IsShared("xero"));
        Assert.True(SharedSettings.IsShared("email-templates"));
        Assert.False(SharedSettings.IsShared("appearance"));
        Assert.False(SharedSettings.IsShared("sidebar"));
        Assert.False(SharedSettings.IsShared("chatgpt-tunnel"));
        Assert.False(SharedSettings.IsShared(SharedSettings.StampsName));
    }

    [Fact]
    public void The_later_change_wins_and_on_a_tie_the_owners_copy_does()
    {
        Assert.True(SharedSettings.TakeArriving(null, At, ownerHere: true));
        Assert.True(SharedSettings.TakeArriving(At, At.AddMinutes(1), ownerHere: true));
        Assert.False(SharedSettings.TakeArriving(At.AddMinutes(1), At, ownerHere: false));
        // Set before syncing, neither has a time: the partner takes the owner's, the owner keeps theirs.
        Assert.True(SharedSettings.TakeArriving(DateTimeOffset.MinValue, DateTimeOffset.MinValue, ownerHere: false));
        Assert.False(SharedSettings.TakeArriving(DateTimeOffset.MinValue, DateTimeOffset.MinValue, ownerHere: true));
    }

    [Fact]
    public void The_server_takes_any_well_formed_kind_so_a_newer_app_works_with_an_older_server()
    {
        Assert.All(SyncKinds.All, k => Assert.True(SyncLimits.KindIsFine(k)));
        Assert.True(SyncLimits.KindIsFine("somethingnew"));
        Assert.False(SyncLimits.KindIsFine(""));
        Assert.False(SyncLimits.KindIsFine(null));
        Assert.False(SyncLimits.KindIsFine("Task"));
        Assert.False(SyncLimits.KindIsFine("chat part"));
        Assert.False(SyncLimits.KindIsFine(new string('a', 21)));
    }
}
