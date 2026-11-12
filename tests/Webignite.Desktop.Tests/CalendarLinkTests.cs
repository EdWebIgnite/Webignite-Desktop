using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>An event added in the calendar can carry the link to join it, and shows Join when it does.</summary>
public sealed class CalendarLinkTests
{
    [Fact]
    public Task An_event_added_with_a_link_can_be_joined_from_the_day() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var calendar = new CalendarViewModel(app.Services) { NewTitle = "Stand-up", NewTime = "9:00", NewLink = "meet.google.com/abc-defg-hij" };

        calendar.AddEventCommand.Execute(null);

        var saved = Assert.Single(app.Services.Store.ListEvents(calendar.SelectedDate, calendar.SelectedDate));
        Assert.Equal("https://meet.google.com/abc-defg-hij", saved.JoinLink);
        Assert.True(Assert.Single(calendar.SelectedItems).CanJoin);
        Assert.Equal("", calendar.NewLink);
    });

    [Fact]
    public Task A_link_that_isnt_a_web_address_is_asked_for_again_and_nothing_is_added() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var calendar = new CalendarViewModel(app.Services) { NewTitle = "Stand-up", NewLink = "the boardroom" };

        calendar.AddEventCommand.Execute(null);

        Assert.Empty(app.Services.Store.ListEvents(calendar.SelectedDate, calendar.SelectedDate));
        Assert.StartsWith("That meeting link isn't a web address", calendar.FormMessage, StringComparison.Ordinal);
    });

    [Fact]
    public Task An_event_with_no_link_has_no_Join() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var calendar = new CalendarViewModel(app.Services) { NewTitle = "Lunch" };

        calendar.AddEventCommand.Execute(null);

        Assert.False(Assert.Single(calendar.SelectedItems).CanJoin);
    });
}
