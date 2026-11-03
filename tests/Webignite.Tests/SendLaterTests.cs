using Webignite.Core.Mail;

namespace Webignite.Tests;

/// <summary>Send later, and the few seconds to take a mail back after Send.</summary>
public sealed class SendLaterTests
{
    private static readonly TimeSpan Sast = TimeSpan.FromHours(2);
    private static DateTimeOffset At(int day, int hour) => new(2026, 11, day, hour, 0, 0, Sast);

    private static ScheduledSend Scheduled(uint uid, DateTimeOffset at, string account = "work") =>
        new() { Id = ScheduledSend.NewId(), AccountId = account, DraftUid = uid, SendAt = at };

    [Fact]
    public void Mail_goes_once_its_time_has_come_oldest_first()
    {
        var due = SendLater.Due([Scheduled(3, At(4, 8)), Scheduled(1, At(3, 15)), Scheduled(2, At(9, 8))], At(4, 8));
        Assert.Equal([1u, 3u], due.Select(s => s.DraftUid));
    }

    [Fact]
    public void A_draft_s_send_is_found_by_mailbox_and_number()
    {
        ScheduledSend[] all = [Scheduled(3, At(4, 8)), Scheduled(3, At(5, 8), account: "accounts")];
        Assert.Equal(At(5, 8), SendLater.For(all, "accounts", 3)?.SendAt);
        Assert.Null(SendLater.For(all, "work", 4));
    }

    [Fact]
    public void When_reads_plainly()
    {
        var now = At(3, 10);
        Assert.Equal("today at 15:00", SendLater.When(At(3, 15), now));
        Assert.Equal("tomorrow at 08:00", SendLater.When(At(4, 8), now));
        Assert.Equal("Mon 9 Nov at 08:00", SendLater.When(At(9, 8), now));
    }

    [Fact]
    public void The_countdown_counts_seconds()
    {
        Assert.Equal("Sending to Thandi in 10 seconds.", SendLater.Countdown("Thandi", SendLater.UndoSeconds));
        Assert.Equal("Sending to Thandi in 1 second.", SendLater.Countdown("Thandi", 1));
    }
}
