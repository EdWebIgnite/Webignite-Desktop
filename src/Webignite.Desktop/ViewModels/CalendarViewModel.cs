using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;

namespace Webignite.Desktop.ViewModels;

public partial class CalendarViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices services;
    private DateOnly month;

    public CalendarViewModel(AppServices services)
    {
        this.services = services;
        var today = Today;
        month = Webignite.Core.Dates.MonthStart(today);
        SelectedDate = today;
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static IReadOnlyList<string> WeekdayNames { get; } = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public ObservableCollection<DayCellViewModel> Days { get; } = [];
    public ObservableCollection<AgendaItemViewModel> SelectedItems { get; } = [];

    [ObservableProperty]
    public partial string MonthTitle { get; set; } = "";

    [ObservableProperty]
    public partial DateOnly SelectedDate { get; set; }

    [ObservableProperty]
    public partial string SelectedTitle { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddEventCommand))]
    public partial string NewTitle { get; set; } = "";

    [ObservableProperty]
    public partial string NewTime { get; set; } = "";

    [ObservableProperty]
    public partial string NewLocation { get; set; } = "";

    [ObservableProperty]
    public partial string FormMessage { get; set; } = "";

    public bool SelectedIsEmpty => SelectedItems.Count == 0;

    [RelayCommand]
    private void PreviousMonth() => Show(month.AddMonths(-1));

    [RelayCommand]
    private void NextMonth() => Show(month.AddMonths(1));

    [RelayCommand]
    private void GoToToday()
    {
        SelectedDate = Today;
        Show(Webignite.Core.Dates.MonthStart(Today));
    }

    [RelayCommand]
    private void SelectDay(DayCellViewModel day)
    {
        SelectedDate = day.Date;
        if (day.Date.Month != month.Month)
            Show(Webignite.Core.Dates.MonthStart(day.Date));
        else
            Refresh();
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewTitle);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddEvent()
    {
        TimeOnly? start = null;
        if (!string.IsNullOrWhiteSpace(NewTime))
        {
            if (!CalendarEvents.TryParseTime(NewTime, out var parsed))
            {
                FormMessage = "Type the time like 9:30 or 14:00, or leave it empty for all day.";
                return;
            }
            start = parsed;
        }
        services.Store.SaveEvent(new CalendarEvent
        {
            Id = CalendarEvent.NewId(),
            Title = NewTitle.Trim(),
            Date = SelectedDate,
            Start = start,
            End = start?.AddHours(1),
            Location = string.IsNullOrWhiteSpace(NewLocation) ? null : NewLocation.Trim(),
        });
        NewTitle = NewTime = NewLocation = FormMessage = "";
        Refresh();
    }

    private void DeleteEvent(AgendaItemViewModel item)
    {
        var gone = services.Store.GetEvent(item.Item.SourceId);
        services.Store.DeleteEvent(item.Item.SourceId);
        Refresh();
        if (gone is not null)
            services.OfferUndo($"Deleted {gone.Title}", () =>
            {
                services.Store.SaveEvent(gone);
                Refresh();
            });
    }

    private void Show(DateOnly firstOfMonth)
    {
        month = firstOfMonth;
        Refresh();
    }

    public void Refresh()
    {
        var (first, last) = Agenda.MonthGrid(month.Year, month.Month);
        var items = Agenda.Build(first, last, services.Store.ListEvents(first, last), services.Store.ListTasks(), services.Store.List());
        var byDay = items.ToLookup(i => i.Date);

        MonthTitle = month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        Days.Clear();
        for (var day = first; day <= last; day = day.AddDays(1))
            Days.Add(new DayCellViewModel(day, month.Month, Today, SelectedDate, byDay[day].Select(i => new AgendaItemViewModel(i)).ToList()));

        SelectedTitle = SelectedDate == Today ? $"Today, {SelectedDate:d MMMM}" : SelectedDate.ToString("dddd, d MMMM");
        SelectedItems.Clear();
        var selected = SelectedDate >= first && SelectedDate <= last
            ? byDay[SelectedDate]
            : Agenda.Build(SelectedDate, SelectedDate, services.Store.ListEvents(SelectedDate, SelectedDate), services.Store.ListTasks(), services.Store.List());
        foreach (var item in selected)
            SelectedItems.Add(new AgendaItemViewModel(item, DeleteEvent));
        OnPropertyChanged(nameof(SelectedIsEmpty));
    }
}

public sealed class DayCellViewModel(DateOnly date, int month, DateOnly today, DateOnly selected, IReadOnlyList<AgendaItemViewModel> items)
{
    private const int Shown = 3;

    public DateOnly Date { get; } = date;
    public string DayNumber => Date.Day.ToString();
    public bool IsToday => Date == today;
    public bool IsSelected => Date == selected;
    public bool IsOtherMonth => Date.Month != month;
    public IReadOnlyList<AgendaItemViewModel> Items { get; } = items.Take(Shown).ToList();
    public string More => items.Count > Shown ? $"+{items.Count - Shown} more" : "";
    public bool HasMore => items.Count > Shown;
}
