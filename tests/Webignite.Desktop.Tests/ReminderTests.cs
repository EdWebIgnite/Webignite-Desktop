using Webignite.Core.Calendar;
using Webignite.Core.Projects;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Reminders: a task due at a time is announced a little before, once, in the app and to Windows when the app is behind other windows.</summary>
public sealed class ReminderTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 13, 45, 0);

    private static (MainViewModel Main, ProjectTask Task, List<string> Notes, List<string> Windows) Open(TestApp app, bool inFront = true)
    {
        var notes = new List<string>();
        var windows = new List<string>();
        app.Services.ShowNote = (title, text, _) => notes.Add(title + " | " + text);
        app.Services.ShowWindowsNote = (title, text, _) => windows.Add(title + " | " + text);
        app.Services.IsInFront = () => inFront;
        var project = app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Bosveld Builders new website" }).Saved!;
        var task = app.Services.Projects.AddTask(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Send the sitemap", AssignedTo = app.Services.Me().Id,
            Due = DateOnly.FromDateTime(Now), DueTime = new TimeOnly(14, 0),
        }).Saved!;
        return (new MainViewModel(app.Services), task, notes, windows);
    }

    [Fact]
    public Task A_task_due_at_a_time_is_announced_ten_minutes_before_and_only_once() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (main, _, notes, _) = Open(app);

        Assert.Empty(main.Remind(Now));
        Assert.Single(main.Remind(Now.AddMinutes(5)));
        Assert.Empty(main.Remind(Now.AddMinutes(6)));

        Assert.Equal("Send the sitemap | Due at 14:00, in 10 minutes · Bosveld Builders new website", Assert.Single(notes).Replace("2:00 PM", "14:00", StringComparison.Ordinal));
    });

    [Fact]
    public Task Behind_other_windows_it_is_a_Windows_notification_too_and_in_front_it_is_not() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (main, _, notes, windows) = Open(app, inFront: false);
        main.Remind(Now.AddMinutes(5));
        Assert.Single(notes);
        Assert.Single(windows);

        using var other = new TestApp();
        var (front, _, frontNotes, frontWindows) = Open(other, inFront: true);
        front.Remind(Now.AddMinutes(5));
        Assert.Single(frontNotes);
        Assert.Empty(frontWindows);
    });

    [Fact]
    public Task Windows_notifications_can_be_switched_off_and_so_can_reminders() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (main, _, notes, windows) = Open(app, inFront: false);
        app.Services.Settings.Reminders = app.Services.Settings.Reminders with { Windows = false };
        main.Remind(Now.AddMinutes(5));
        Assert.Single(notes);
        Assert.Empty(windows);

        using var other = new TestApp();
        var (quiet, _, quietNotes, _) = Open(other);
        other.Services.Settings.Reminders = other.Services.Settings.Reminders with { On = false };
        Assert.Empty(quiet.Remind(Now.AddMinutes(5)));
        Assert.Empty(quietNotes);
    });

    [Fact]
    public Task A_task_given_a_time_just_now_is_picked_up_at_the_next_look() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (main, task, notes, _) = Open(app);
        Assert.Empty(main.Remind(Now));

        // Moved to five minutes from now: with ten minutes' warning, that is already due to be said.
        app.Services.Projects.ChangeTask(task.Id, t => t with { DueTime = new TimeOnly(13, 50) });

        Assert.Single(main.Remind(Now.AddMinutes(1)));
        Assert.Single(notes);
    });

    [Fact]
    public Task A_calendar_event_with_a_start_time_is_announced() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (main, task, notes, _) = Open(app);
        app.Services.Projects.ChangeTask(task.Id, t => t with { DueTime = null });
        app.Services.Store.SaveEvent(new CalendarEvent { Id = "e1", Title = "Kick-off with Amos", Date = DateOnly.FromDateTime(Now), Start = new TimeOnly(13, 50), Location = "Teams" });
        main.RemindersChanged();

        var due = Assert.Single(main.Remind(Now));

        Assert.Equal(ReminderKind.Event, due.Kind);
        Assert.StartsWith("Kick-off with Amos | Starts at ", Assert.Single(notes), StringComparison.Ordinal);
        Assert.EndsWith("in 5 minutes · Teams", notes[0], StringComparison.Ordinal);
    });

    [Fact]
    public Task Settings_has_the_choices_and_they_are_kept() => Ui.Run(() =>
    {
        using var app = new TestApp();
        using var settings = new SettingsViewModel(app.Services);
        settings.Open("reminders");
        Assert.True(settings.ShowReminders);
        Assert.Equal(10, settings.ReminderLead.Minutes);

        settings.ReminderLead = SettingsViewModel.ReminderLeads.Single(l => l.Minutes == 30);
        settings.WindowsNotes = false;

        Assert.Equal(30, app.Services.Settings.Reminders.MinutesBefore);
        Assert.False(app.Services.Settings.Reminders.Windows);
        Assert.Equal("30 minutes before", settings.ReminderLead.ToString());
    });
}
