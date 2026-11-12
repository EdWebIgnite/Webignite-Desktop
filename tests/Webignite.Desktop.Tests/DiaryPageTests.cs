using Webignite.Core.Calendar;
using Webignite.Core.Tasks;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>The diary writes into the calendar and the to-dos, and keeps only its notes for itself.</summary>
public sealed class DiaryPageTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static DiaryPageViewModel PageFor(DiaryViewModel diary, DateOnly day) => diary.Left.Date == day ? diary.Left : diary.Right;

    [Fact]
    public Task It_opens_at_today_with_the_next_or_last_day_beside_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var diary = new DiaryViewModel(app.Services);

        Assert.Equal(diary.Left.Date.AddDays(1), diary.Right.Date);
        Assert.True(PageFor(diary, Today).IsToday);
        Assert.False(diary.HoldsWork);
    });

    [Fact]
    public Task Something_written_on_an_hours_line_is_an_event_at_that_hour() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var diary = new DiaryViewModel(app.Services);
        var line = PageFor(diary, Today).Lines.Single(l => l.Hour.Hour == 9);

        line.NewText = "Call the printers";
        Assert.True(diary.HoldsWork);
        line.AddCommand.Execute(null);

        var saved = Assert.Single(app.Services.Store.ListEvents(Today, Today));
        Assert.Equal("Call the printers", saved.Title);
        Assert.Equal(new TimeOnly(9, 0), saved.Start);
        Assert.Equal("Call the printers", Assert.Single(PageFor(diary, Today).Lines.Single(l => l.Hour.Hour == 9).Entries).Title);
        Assert.False(diary.HoldsWork);
    });

    [Fact]
    public Task A_time_typed_first_is_the_time_it_starts() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var diary = new DiaryViewModel(app.Services);
        var line = PageFor(diary, Today).Lines.Single(l => l.Hour.Hour == 9);

        line.NewText = "9:45 Stand-up";
        line.AddCommand.Execute(null);

        var saved = Assert.Single(app.Services.Store.ListEvents(Today, Today));
        Assert.Equal("Stand-up", saved.Title);
        Assert.Equal(new TimeOnly(9, 45), saved.Start);
    });

    [Fact]
    public Task A_to_do_written_on_a_page_is_due_that_day_and_ticks_off_for_good() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var diary = new DiaryViewModel(app.Services);
        var page = PageFor(diary, Today);

        page.NewTodo = "Renew the SSL";
        page.AddTodoCommand.Execute(null);
        var todo = Assert.Single(app.Services.Store.ListTasks());
        Assert.Equal(Today, todo.Due);

        Assert.Single(PageFor(diary, Today).Untimed).IsDone = true;

        Assert.True(Assert.Single(app.Services.Store.ListTasks()).Done);
        Assert.True(Assert.Single(PageFor(diary, Today).Untimed).IsDone);
    });

    [Fact]
    public Task Notes_stay_with_their_day_when_the_page_is_turned() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var diary = new DiaryViewModel(app.Services);
        PageFor(diary, Today).Notes = "Ask about the photos";
        Assert.True(diary.HoldsWork);

        diary.NextPagesCommand.Execute(null);
        Assert.Equal("", diary.Left.Notes);
        Assert.Equal("Ask about the photos", app.Services.Store.DiaryNote(Today));

        diary.PreviousPagesCommand.Execute(null);
        Assert.Equal("Ask about the photos", PageFor(diary, Today).Notes);

        PageFor(diary, Today).Notes = "";
        diary.Keep();
        Assert.Equal("", app.Services.Store.DiaryNote(Today));
    });

    [Fact]
    public Task Turning_the_page_moves_two_days_and_a_day_opens_on_its_own_spread() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var diary = new DiaryViewModel(app.Services);
        var was = diary.Left.Date;

        diary.NextPagesCommand.Execute(null);
        Assert.Equal(was.AddDays(2), diary.Left.Date);

        diary.Show(Today.AddDays(31));
        Assert.Contains(Today.AddDays(31), new[] { diary.Left.Date, diary.Right.Date });

        diary.GoToTodayCommand.Execute(null);
        Assert.Equal(was, diary.Left.Date);
    });

    [Fact]
    public Task An_event_with_a_meeting_link_can_be_joined_and_removed() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Services.Store.SaveEvent(new CalendarEvent { Id = "e1", Title = "Catch-up", Date = Today, Start = new TimeOnly(14, 0), JoinLink = "https://meet.google.com/abc-defg-hij" });
        app.Services.Store.SaveTask(new TaskItem { Id = TaskItem.NewId(), Title = "Not for today", Due = Today.AddDays(9) });
        var diary = new DiaryViewModel(app.Services);

        var entry = Assert.Single(PageFor(diary, Today).Lines.Single(l => l.Hour.Hour == 14).Entries);
        Assert.True(entry.CanJoin);
        Assert.Empty(PageFor(diary, Today).Untimed);

        entry.DeleteCommand.Execute(null);
        Assert.Empty(app.Services.Store.ListEvents(Today, Today));
    });
}
