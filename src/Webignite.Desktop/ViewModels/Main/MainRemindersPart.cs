using Webignite.Core.Calendar;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Reminders: a task due at a time and a calendar event starting at one are announced a little before, once
/// (rules in <see cref="Reminders"/>). Looked at each minute by <see cref="BackgroundJobs"/>.
/// </summary>
public partial class MainViewModel
{
    // What is coming up, read every few minutes and whenever tasks change, so each minute's look is a glance at a short list.
    private IReadOnlyList<Reminder> coming = [];
    private DateTime comingReadAt = DateTime.MinValue;
    private bool comingStale = true;
    private bool watchingTasks;
    private static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LookAhead = TimeSpan.FromMinutes(20);

    /// <summary>Shows the reminders whose time has come. Returns those shown, for trying it without a clock.</summary>
    public IReadOnlyList<Reminder> Remind(DateTime now)
    {
        var settings = services.Settings.Reminders;
        if (!settings.On)
            return [];
        if (!watchingTasks)
        {
            watchingTasks = true;
            services.Projects.Changed += _ => comingStale = true;
        }
        if (comingStale || now - comingReadAt >= ReadEvery || now < comingReadAt)
        {
            var today = DateOnly.FromDateTime(now);
            var names = services.Projects.Projects().ToDictionary(p => p.Id, p => p.Name);
            // Events are read each time as well: one added a minute ago for ten minutes' time must not be missed.
            coming = Reminders.Upcoming(services.Projects.Mine(services.Me().Id), names, services.Store.ListEvents(today.AddDays(-1), today.AddDays(1)),
                now, settings.MinutesBefore, LookAhead + ReadEvery);
            comingReadAt = now;
            comingStale = false;
        }
        var due = Reminders.Due(coming, now, settings.Shown);
        if (due.Count == 0)
            return due;
        services.Settings.Reminders = settings with { Shown = Reminders.Remembered(settings.Shown, due, now) };
        foreach (var reminder in due)
        {
            var shown = reminder;
            services.Notify(shown.Title, Reminders.Text(shown, now, System.Globalization.CultureInfo.CurrentCulture), () =>
            {
                if (shown.Kind == ReminderKind.Task)
                    services.Go.ProjectTask(shown.TargetId);
                else
                    services.Go.Diary(DateOnly.FromDateTime(shown.At));
            });
        }
        if (WindowIsActive?.Invoke() != true)
            FlashTaskbar?.Invoke();
        return due;
    }

    /// <summary>After the calendar changes (an event added from a mail or the quick note): what is coming up is read afresh at the next look.</summary>
    public void RemindersChanged() => comingStale = true;
}
