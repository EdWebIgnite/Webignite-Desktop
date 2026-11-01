using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Core.Time;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class TimeTests
{
    [Theory]
    [InlineData("1.5", 90)]
    [InlineData("1,5", 90)]
    [InlineData("2", 120)]
    [InlineData("1:30", 90)]
    [InlineData("0:45", 45)]
    [InlineData("90m", 90)]
    [InlineData("90 min", 90)]
    [InlineData("1h30", 90)]
    [InlineData("1 h 30 min", 90)]
    [InlineData("2h", 120)]
    [InlineData("1.25h", 75)]
    public void Typed_times_read_as_minutes(string typed, int minutes) => Assert.Equal(minutes, TimeRules.ParseMinutes(typed));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("17")]
    [InlineData("1:75")]
    public void Nonsense_zero_and_overlong_times_are_refused(string typed) => Assert.Null(TimeRules.ParseMinutes(typed));

    [Fact]
    public void Times_show_in_hours_and_minutes()
    {
        Assert.Equal("45 min", TimeRules.Format(45));
        Assert.Equal("1 h", TimeRules.Format(60));
        Assert.Equal("2 h 15 min", TimeRules.Format(135));
        Assert.Equal("1:05:09", TimeRules.Clock(new TimeSpan(1, 5, 9)));
    }

    [Fact]
    public void Stopping_the_timer_makes_an_entry_dated_when_it_started()
    {
        var started = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var entry = TimeRules.Stop(new RunningTimer { Started = started, Note = " Homepage ", ClientId = "c1", ProjectId = "p1" }, started.AddMinutes(84).AddSeconds(40))!;

        Assert.Equal(85, entry.Minutes);
        Assert.Equal(DateOnly.FromDateTime(started.ToLocalTime().DateTime), entry.Date);
        Assert.Equal("Homepage", entry.Note);
        Assert.Equal("p1", entry.ProjectId);
        Assert.Null(TimeRules.Stop(new RunningTimer { Started = started }, started.AddSeconds(20)));
        // A project without a client means nothing.
        Assert.Null(TimeRules.Stop(new RunningTimer { Started = started, ProjectId = "p1" }, started.AddMinutes(5))!.ProjectId);
        // Left running overnight: capped.
        Assert.Equal(TimeRules.MaxMinutes, TimeRules.Stop(new RunningTimer { Started = started }, started.AddDays(2))!.Minutes);
    }

    [Fact]
    public void Weeks_start_on_Monday()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), TimeRules.WeekStart(new DateOnly(2026, 11, 1)));
        Assert.Equal(new DateOnly(2026, 11, 2), TimeRules.WeekStart(new DateOnly(2026, 11, 2)));
    }

    private static readonly Client[] Clients =
    [
        new() { Id = "c1", Name = "Karoo Coffee Roasters", Projects = [new() { Id = "p1", Name = "New website" }] },
        new() { Id = "c2", Name = "Fynbos Physio" },
    ];

    private static TimeEntry Entry(int minutes, string? client = null, string? project = null) =>
        new() { Id = TimeEntry.NewId(), Date = Samples.Today, Minutes = minutes, ClientId = client, ProjectId = project };

    [Fact]
    public void Totals_go_per_client_then_project_with_loose_time_last()
    {
        var totals = TimeRules.Totals([Entry(30, "c2"), Entry(60, "c1", "p1"), Entry(45, "c1"), Entry(20), Entry(10)], Clients);

        Assert.Equal(
            [("Karoo Coffee Roasters", 105), ("Karoo Coffee Roasters: New website", 60), ("Fynbos Physio", 30), (TimeRules.NoClient, 30)],
            totals.Select(t => (t.Label, t.Minutes)));
        Assert.Equal("Karoo Coffee Roasters: New website", TimeRules.Describe(Entry(5, "c1", "p1"), Clients));
        Assert.Equal(TimeRules.NoClient, TimeRules.Describe(Entry(5), Clients));
    }

    [Fact]
    public void Entries_and_the_running_timer_are_kept()
    {
        using var store = new LocalStore(Path.Combine(Directory.CreateTempSubdirectory().FullName, "t.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SaveTime(Entry(30, "c1") with { Date = new DateOnly(2026, 10, 30) });
        var kept = store.SaveTime(Entry(15) with { Date = new DateOnly(2026, 11, 1), Note = "Bookkeeping" });
        store.SaveTime(Entry(15) with { Date = new DateOnly(2026, 9, 1) });

        var list = store.ListTime(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30));
        Assert.Equal(["Bookkeeping", ""], list.Select(e => e.Note));
        store.DeleteTime(kept.Id);
        Assert.Single(store.ListTime(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30)));

        Assert.Null(store.RunningTimer);
        store.SetRunningTimer(new RunningTimer { Started = DateTimeOffset.UtcNow, Note = "Logo" });
        Assert.Equal("Logo", store.RunningTimer!.Note);
        store.SetRunningTimer(null);
        Assert.Null(store.RunningTimer);
    }
}
