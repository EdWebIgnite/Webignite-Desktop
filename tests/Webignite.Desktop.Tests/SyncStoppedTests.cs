using Webignite.Desktop.ViewModels;
using Webignite.Services;

namespace Webignite.Desktop.Tests;

/// <summary>
/// A PC whose sync has stopped says so in the chat window and on Overview. Nothing else on that PC looks wrong, and
/// the other person only sees this one as "last seen" days ago.
/// </summary>
public sealed class SyncStoppedTests
{
    // A PC that synced before and is signed out now: it tries nothing, so there is no failed sync to go by.
    private static void SyncedBeforeAndSignedOut(TestApp app) =>
        app.Services.Store.SetSetting("sync", new SyncState { WorkspaceId = "w1", LastSynced = new DateTimeOffset(2026, 11, 20, 16, 49, 0, TimeSpan.Zero) });

    [Fact]
    public Task A_PC_that_never_synced_says_nothing() => Ui.Run(() =>
    {
        using var app = new TestApp();
        Assert.Null(app.Services.SyncTrouble);
        using var chat = new ChatViewModel(app.Services);
        Assert.False(chat.HasSyncTrouble);
    });

    [Fact]
    public Task The_chat_window_says_sync_has_stopped_and_since_when() => Ui.Run(() =>
    {
        using var app = new TestApp();
        SyncedBeforeAndSignedOut(app);
        using var chat = new ChatViewModel(app.Services);

        Assert.True(chat.HasSyncTrouble);
        Assert.StartsWith("Chat and shared projects stopped syncing on this PC at ", chat.SyncTroubleLine, StringComparison.Ordinal);
        Assert.EndsWith("You're signed out on this PC.", chat.SyncTroubleLine, StringComparison.Ordinal);
    });

    [Fact]
    public Task Overview_says_it_first_under_Needs_you() => Ui.Run(() =>
    {
        using var app = new TestApp();
        SyncedBeforeAndSignedOut(app);
        var overview = new OverviewViewModel(app.Services);
        if (overview.HasMoreSummary)
            overview.ToggleSummaryCommand.Execute(null);

        var first = overview.Summary[0];
        Assert.StartsWith("Chat and shared projects stopped syncing on this PC at ", first.Text, StringComparison.Ordinal);
        Assert.EndsWith("Open Settings, Account and sync.", first.Text, StringComparison.Ordinal);
        overview.Dispose();
    });
}
