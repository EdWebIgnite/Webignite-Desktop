using Webignite.Core.Mail;

namespace Webignite.Tests;

/// <summary>Snooze: put a mail away until later; it comes back to the top, unread.</summary>
public sealed class SnoozeTests
{
    private static readonly TimeSpan Sast = TimeSpan.FromHours(2);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, Sast);

    [Fact]
    public void A_morning_offers_later_today_this_evening_tomorrow_and_next_monday()
    {
        // Tuesday 3 November 2026, 09:40.
        var choices = Snooze.Choices(At(3, 9, 40));
        Assert.Equal(
        [
            ("Later today, 12:00", At(3, 12)),
            ("This evening, 18:00", At(3, 18)),
            ("Tomorrow morning, Wed 4 Nov", At(4, 8)),
            ("Next week, Mon 9 Nov", At(9, 8)),
        ], choices);
    }

    [Fact]
    public void Late_in_the_day_only_tomorrow_and_next_week_are_left()
    {
        var choices = Snooze.Choices(At(3, 17, 30));
        Assert.Equal(["Tomorrow morning, Wed 4 Nov", "Next week, Mon 9 Nov"], choices.Select(c => c.Label));
    }

    [Fact]
    public void On_a_sunday_next_week_is_tomorrow_so_it_is_offered_once()
    {
        var choices = Snooze.Choices(At(8, 20)); // Sunday 8 November
        Assert.Equal(["Tomorrow morning, Mon 9 Nov"], choices.Select(c => c.Label));
    }

    [Fact]
    public void A_picked_day_and_hour_must_be_in_the_future()
    {
        var now = At(3, 10);
        Assert.Equal(At(5, 9), Snooze.At(new DateOnly(2026, 11, 5), 9, now));
        Assert.Null(Snooze.At(new DateOnly(2026, 11, 3), 8, now));
    }

    [Fact]
    public void The_note_says_until_when_in_plain_words()
    {
        var now = At(3, 10);
        Assert.Equal("until 13:00 today", Snooze.Until(At(3, 13), now));
        Assert.Equal("until tomorrow at 08:00", Snooze.Until(At(4, 8), now));
        Assert.Equal("until Mon 9 Nov at 08:00", Snooze.Until(At(9, 8), now));
    }

    private static SnoozedMail Snoozed(string id, DateTimeOffset until) =>
        new() { AccountId = "work", MessageId = id, BackTo = "INBOX", Until = until };

    private static MailSummary Mail(string id, uint uid, DateTimeOffset date) =>
        new() { AccountId = "work", Folder = "INBOX.Snoozed", Uid = uid, MessageId = id, Date = date };

    [Fact]
    public void Snoozed_mail_is_due_once_its_time_comes_and_is_found_by_message_id()
    {
        var now = At(4, 8);
        var due = Snooze.Due([Snoozed("<a>", At(4, 8)), Snoozed("<b>", At(9, 8))], now);
        Assert.Equal(["<a>"], due.Select(s => s.MessageId));
        Assert.Equal(7u, Snooze.Find(due[0], [Mail("<z>", 5, now), Mail(" <a> ", 7, now)])?.Uid);
        Assert.Null(Snooze.Find(due[0], [Mail("<z>", 5, now)]));
    }

    [Fact]
    public void Mail_back_from_snooze_sits_at_the_top_for_two_days()
    {
        var mail = Mail("<a>", 7, At(1, 9));
        WokenMail[] woken = [new("work", "<a>", At(4, 8))];
        Assert.Equal(At(4, 8), Snooze.Arrived(mail, woken, At(4, 10)));
        Assert.Equal(At(1, 9), Snooze.Arrived(mail, woken, At(7, 10)));
        Assert.Equal(At(1, 9), Snooze.Arrived(mail with { MessageId = null }, woken, At(4, 10)));
        Assert.Empty(Snooze.Tidy(woken, At(7, 10)));
    }

    [Fact]
    public void Conversations_follow_the_arrival_order()
    {
        var older = Mail("<a>", 1, At(1, 9)) with { Subject = "Logo" };
        var newer = Mail("<b>", 2, At(2, 9)) with { Subject = "Hosting" };
        var threads = MailThreads.Group([older, newer], m => m.MessageId == "<a>" ? At(4, 8) : m.Date);
        Assert.Equal("<a>", threads[0].Latest.MessageId);
    }
}
