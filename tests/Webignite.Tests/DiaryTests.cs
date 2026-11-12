using Webignite.Core.Calendar;
using Webignite.Core.Renewals;
using Webignite.Core.Retainers;

namespace Webignite.Tests;

public class DiaryTests
{
    private static readonly DateOnly Day = new(2026, 11, 12);
    private static readonly TimeZoneInfo Sast = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");

    [Fact]
    public void A_page_runs_from_seven_to_six_with_each_thing_on_its_hour()
    {
        var page = Diary.Day(Day,
        [
            new AgendaItem(Day, AgendaKind.Event, "Kick-off", new TimeOnly(10, 30), "e1"),
            new AgendaItem(Day, AgendaKind.Task, "Send sitemap", null, "t1"),
            new AgendaItem(Day.AddDays(1), AgendaKind.Event, "Tomorrow's", new TimeOnly(9, 0), "e2"),
        ]);
        Assert.Equal(12, page.Lines.Count);
        Assert.Equal(new TimeOnly(7, 0), page.Lines[0].Hour);
        Assert.Equal("Kick-off", Assert.Single(page.Lines.Single(l => l.Hour.Hour == 10).Items).Title);
        Assert.Equal("Send sitemap", Assert.Single(page.Untimed).Title);
    }

    [Fact]
    public void An_early_or_late_thing_makes_the_page_longer_so_it_is_never_left_off()
    {
        var page = Diary.Day(Day,
        [
            new AgendaItem(Day, AgendaKind.Event, "Flight", new TimeOnly(5, 15), "e1"),
            new AgendaItem(Day, AgendaKind.Event, "Dinner", new TimeOnly(20, 0), "e2"),
        ]);
        Assert.Equal(5, page.Lines[0].Hour.Hour);
        Assert.Equal(20, page.Lines[^1].Hour.Hour);
    }

    [Fact]
    public void Both_days_of_a_spread_open_the_same_two_pages()
    {
        var (left, right) = Diary.Spread(Day);
        Assert.Equal(right, left.AddDays(1));
        Assert.Contains(Day, new[] { left, right });
        Assert.Equal((left, right), Diary.Spread(left));
        Assert.Equal((left, right), Diary.Spread(right));
        Assert.Equal(left.AddDays(2), Diary.Spread(right.AddDays(1)).Left);
    }

    [Fact]
    public void Renewals_and_retainer_invoices_are_noted_on_their_days()
    {
        var renewals = new[] { new Renewal { Id = "r1", Name = "karoocoffee.co.za", ClientName = "Karoo Coffee", RenewsOn = Day } };
        var retainers = new[]
        {
            new Retainer { Id = "m1", ClientName = "Fynbos Physio", Title = "Monthly SEO", Lines = [], DayOfMonth = 13, Starts = new DateOnly(2026, 1, 1) },
            new Retainer { Id = "m2", ClientName = "Paused", Title = "Hosting", Lines = [], DayOfMonth = 13, Starts = new DateOnly(2026, 1, 1), Paused = true },
            new Retainer { Id = "m3", ClientName = "Not yet", Title = "Care plan", Lines = [], DayOfMonth = 13, Starts = new DateOnly(2027, 1, 1) },
        };
        var dates = Diary.MoneyDates(Day, Day.AddDays(1), renewals, retainers);
        Assert.Equal(2, dates.Count);
        Assert.Contains(dates, d => d is { Kind: AgendaKind.Renewal, Title: "karoocoffee.co.za renews (Karoo Coffee)" } && d.Date == Day);
        Assert.Contains(dates, d => d is { Kind: AgendaKind.Retainer, Title: "Monthly SEO invoice goes out (Fynbos Physio)" } && d.Date == Day.AddDays(1));
    }

    private const string TeamsInvite = """
        BEGIN:VCALENDAR
        METHOD:REQUEST
        BEGIN:VEVENT
        SUMMARY:Website kick-off
        DTSTART:20261116T080000Z
        DTEND:20261116T090000Z
        LOCATION:Microsoft Teams Meeting
        DESCRIPTION:Join here\nhttps://teams.microsoft.com/l/meetup-join/19%3ameeting_abc/0?context=%7b%22Tid%22%7d\n
        BEGIN:VALARM
        TRIGGER:-PT15M
        DESCRIPTION:Reminder
        END:VALARM
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void A_calendar_invite_gives_the_meeting_in_this_pcs_time()
    {
        var invite = MeetingInvites.FromCalendar(TeamsInvite.Replace("        ", ""), Sast)!;
        Assert.Equal("Website kick-off", invite.Title);
        Assert.Equal(new DateOnly(2026, 11, 16), invite.Date);
        Assert.Equal(new TimeOnly(10, 0), invite.Start);
        Assert.Equal(new TimeOnly(11, 0), invite.End);
        Assert.Equal("Microsoft Teams Meeting", invite.Location);
        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc/0", invite.JoinLink);
        Assert.True(invite.FromInvite);
    }

    [Fact]
    public void A_long_line_carried_over_and_an_all_day_date_are_read()
    {
        var invite = MeetingInvites.FromCalendar("BEGIN:VEVENT\r\nSUMMARY:Year-end \r\n function\\, Stellenbosch\r\nDTSTART;VALUE=DATE:20261204\r\nEND:VEVENT", Sast)!;
        Assert.Equal("Year-end function, Stellenbosch", invite.Title);
        Assert.Equal(new DateOnly(2026, 12, 4), invite.Date);
        Assert.Null(invite.Start);
    }

    [Fact]
    public void A_mail_with_only_a_meeting_link_gives_a_meeting_to_check()
    {
        var invite = MeetingInvites.InMail("RE: Catch-up on the new site", "Hi Eddie, let's talk on 17 November at 14:30. https://us02web.zoom.us/j/8123456789?pwd=abc.", null, null, Day, Sast)!;
        Assert.Equal("Catch-up on the new site", invite.Title);
        Assert.Equal(new DateOnly(2026, 11, 17), invite.Date);
        Assert.Equal(new TimeOnly(14, 30), invite.Start);
        Assert.Equal("https://us02web.zoom.us/j/8123456789?pwd=abc", invite.JoinLink);
        Assert.False(invite.FromInvite);
    }

    [Theory]
    [InlineData("See you tomorrow at 9am", 2026, 11, 13)]
    [InlineData("On Jan 5 we meet", 2027, 1, 5)]
    [InlineData("Booked for 2026-12-01", 2026, 12, 1)]
    public void A_day_named_in_the_text_is_found(string text, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), MeetingInvites.DateIn(text, Day));

    [Fact]
    public void A_mail_with_no_invite_and_no_meeting_link_has_no_meeting() =>
        Assert.Null(MeetingInvites.InMail("Quote", "Please see https://webignite.co.za on 14 November at 10:00", null, null, Day, Sast));

    [Fact]
    public void A_meet_link_in_html_is_found_with_its_ampersands_put_right() =>
        Assert.Equal("https://meet.google.com/abc-defg-hij?authuser=0&hs=1", MeetingInvites.JoinLink("<a href=\"https://meet.google.com/abc-defg-hij?authuser=0&amp;hs=1\">Join</a>"));

    [Theory]
    [InlineData("https://whereby.com/webignite-room")]
    [InlineData("https://meet.jit.si/WebigniteStandup")]
    [InlineData("https://teams.live.com/meet/9361234567890?p=abc")]
    public void Other_meeting_services_are_known_too(string link) => Assert.Equal(link, MeetingInvites.JoinLink("Join us at " + link + " tomorrow"));

    [Theory]
    [InlineData("meet.google.com/abc-defg-hij", "https://meet.google.com/abc-defg-hij")]
    [InlineData(" https://example.co.za/room/12 ", "https://example.co.za/room/12")]
    public void A_link_the_person_pastes_is_kept_whatever_service_it_is(string typed, string kept) => Assert.Equal(kept, MeetingInvites.TypedLink(typed));

    [Theory]
    [InlineData("")]
    [InlineData("the boardroom")]
    [InlineData("ftp://example.co.za/file")]
    public void What_isnt_a_web_address_is_not_a_meeting_link(string typed) => Assert.Null(MeetingInvites.TypedLink(typed));
}
