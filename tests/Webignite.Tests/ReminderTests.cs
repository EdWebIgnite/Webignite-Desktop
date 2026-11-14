using System.Globalization;
using Webignite.Core.Calendar;
using Webignite.Core.Projects;

namespace Webignite.Tests;

public sealed class ReminderTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 13, 45, 0);
    private static readonly Dictionary<string, string> Names = new() { ["p1"] = "Bosveld Builders new website" };

    private static ProjectTask Job(string id, TimeOnly? time, DateOnly? day = null, bool done = false) =>
        new() { Id = id, ProjectId = "p1", Title = "Send the sitemap", Due = day ?? DateOnly.FromDateTime(Now), DueTime = time, Done = done };

    private static CalendarEvent Meeting(string id, TimeOnly? start, string? where = null) =>
        new() { Id = id, Title = "Kick-off with Amos", Date = DateOnly.FromDateTime(Now), Start = start, Location = where };

    private static IReadOnlyList<Reminder> Upcoming(IEnumerable<ProjectTask> tasks, IEnumerable<CalendarEvent> events, int before = 10) =>
        Reminders.Upcoming(tasks, Names, events, Now, before, TimeSpan.FromMinutes(30));

    [Fact]
    public void A_task_due_at_a_time_and_an_event_starting_at_one_are_coming_up()
    {
        var upcoming = Upcoming([Job("t1", new TimeOnly(14, 0))], [Meeting("e1", new TimeOnly(13, 50), "Teams")]);

        Assert.Equal(["Kick-off with Amos", "Send the sitemap"], upcoming.Select(r => r.Title));
        Assert.Equal(ReminderKind.Event, upcoming[0].Kind);
        Assert.Equal(new DateTime(2026, 11, 16, 13, 50, 0), upcoming[1].From);
        Assert.Equal("Bosveld Builders new website", upcoming[1].Detail);
    }

    [Fact]
    public void Things_with_no_time_done_tasks_and_ones_far_off_have_no_reminder()
    {
        var upcoming = Upcoming(
            [Job("none", null), Job("done", new TimeOnly(14, 0), done: true), Job("later", new TimeOnly(17, 0)), Job("tomorrow", new TimeOnly(14, 0), DateOnly.FromDateTime(Now).AddDays(1))],
            [Meeting("allday", null)]);

        Assert.Empty(upcoming);
    }

    [Fact]
    public void A_reminder_shows_once_its_lead_time_has_come_and_only_once()
    {
        var upcoming = Upcoming([Job("t1", new TimeOnly(14, 0))], []);
        var none = new Dictionary<string, DateTime>();

        Assert.Empty(Reminders.Due(upcoming, Now, none));
        var due = Reminders.Due(upcoming, Now.AddMinutes(5), none);
        Assert.Single(due);

        var shown = Reminders.Remembered(none, due, Now.AddMinutes(5));
        Assert.Empty(Reminders.Due(upcoming, Now.AddMinutes(6), shown));
    }

    [Fact]
    public void One_missed_by_under_an_hour_is_still_shown_and_an_older_one_is_not()
    {
        var upcoming = Upcoming([Job("t1", new TimeOnly(13, 0))], []);
        var none = new Dictionary<string, DateTime>();

        Assert.Single(Reminders.Due(upcoming, Now, none));
        Assert.Empty(Reminders.Due(upcoming, Now.AddMinutes(20), none));
    }

    [Fact]
    public void A_task_moved_to_another_time_is_reminded_again()
    {
        var first = Upcoming([Job("t1", new TimeOnly(13, 50))], []);
        var shown = Reminders.Remembered(new Dictionary<string, DateTime>(), first, Now);

        var moved = Upcoming([Job("t1", new TimeOnly(13, 55))], []);

        Assert.Single(Reminders.Due(moved, Now, shown));
    }

    [Fact]
    public void What_was_shown_is_forgotten_after_two_days()
    {
        var old = new Dictionary<string, DateTime> { ["task:old"] = Now.AddDays(-3), ["task:recent"] = Now.AddHours(-5) };

        Assert.Equal(["task:recent"], Reminders.Remembered(old, [], Now).Keys);
    }

    [Theory]
    [InlineData(13, 55, "Due at 13:55, in 10 minutes · Bosveld Builders new website")]
    [InlineData(13, 45, "Due now · Bosveld Builders new website")]
    [InlineData(14, 45, "Due at 14:45, in an hour · Bosveld Builders new website")]
    [InlineData(13, 15, "Was due at 13:15 · Bosveld Builders new website")]
    public void A_reminder_says_when_in_plain_words(int hour, int minute, string text)
    {
        var reminder = new Reminder("k", ReminderKind.Task, "t1", "Send the sitemap", new DateTime(2026, 11, 16, hour, minute, 0), Now, "Bosveld Builders new website");

        Assert.Equal(text, Reminders.Text(reminder, Now, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void An_event_says_starts_and_leaves_off_a_place_it_doesnt_have()
    {
        var reminder = new Reminder("k", ReminderKind.Event, "e1", "Kick-off", Now.AddMinutes(5), Now, "");

        Assert.Equal("Starts at 13:50, in 5 minutes", Reminders.Text(reminder, Now, CultureInfo.InvariantCulture));
        Assert.Equal("At the time", Reminders.LeadLabel(0));
        Assert.Equal("1 hour before", Reminders.LeadLabel(60));
        Assert.Equal("15 minutes before", Reminders.LeadLabel(15));
    }
}
