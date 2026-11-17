using Webignite.Core.Calendar;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop;

/// <summary>
/// Today's list, on Overview and as a widget: what is booked or due today, each with a way to clear it right
/// there. A to-do or a project task is ticked off; an event or a date from a quote or invoice is dismissed for
/// the day (it stays in the calendar). Either can be undone from the note that follows.
/// </summary>
public static class TodayList
{
    private static string Key(AgendaItem item) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{item.Date:yyyy-MM-dd}|{item.Kind}|{item.SourceId}");

    /// <summary>What is on today and not done or dismissed, each able to clear itself; <paramref name="changed"/> runs after one does.</summary>
    public static IReadOnlyList<AgendaItemViewModel> Items(AppServices services, Action changed)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dismissed = services.Settings.TodayDismissed.ToHashSet(StringComparer.Ordinal);
        return [.. Agenda.Build(today, today, services.Store.ListEvents(today, today), services.Store.ListTasks(), services.Documents.List(),
                services.Projects.Agenda(today, today, services.Me().Id))
            .Where(i => !i.Done && !dismissed.Contains(Key(i)))
            .Select(i => new AgendaItemViewModel(i, clear: row => Clear(services, row.Item, changed)))];
    }

    private static void Clear(AppServices services, AgendaItem item, Action changed)
    {
        void Told()
        {
            changed();
            services.TodayChanged();
        }
        switch (item.Kind)
        {
            case AgendaKind.Task:
                services.Store.ChangeTask(item.SourceId, t => t with { Done = true, DoneAt = DateTimeOffset.Now });
                Told();
                services.OfferUndo($"Done: {item.Title}", () =>
                {
                    services.Store.ChangeTask(item.SourceId, t => t with { Done = false, DoneAt = null });
                    Told();
                });
                break;
            case AgendaKind.ProjectTask:
                services.Projects.ChangeTask(item.SourceId, t => t with { Done = true });
                Told();
                services.OfferUndo($"Done: {item.Title}", () =>
                {
                    services.Projects.ChangeTask(item.SourceId, t => t with { Done = false });
                    Told();
                });
                break;
            default:
                var key = Key(item);
                // Only today's are kept: yesterday's dismissals have nothing left to hide.
                var day = key[..10];
                services.Settings.TodayDismissed = [.. services.Settings.TodayDismissed.Where(k => k.StartsWith(day, StringComparison.Ordinal) && k != key), key];
                Told();
                services.OfferUndo($"Dismissed for today: {item.Title}", () =>
                {
                    services.Settings.TodayDismissed = [.. services.Settings.TodayDismissed.Where(k => k != key)];
                    Told();
                });
                break;
        }
    }
}
