using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Time;
using Webignite.Core;

namespace Webignite.Desktop.ViewModels;


/// <summary>Which project of the client, or none.</summary>
public sealed record ProjectChoice(string Label, ClientProject? Project)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Time page: a start/stop timer, time typed in by hand, totals per client and project, and the entries.
/// Time can be for a client, one of the client's projects, or nobody at all.
/// </summary>
public partial class TimeViewModel : ViewModelBase, IRefreshable, IDisposable
{
    private readonly AppServices services;
    private IReadOnlyList<Client> clients = [];

    public TimeViewModel(AppServices services)
    {
        this.services = services;
        // The running timer and its clock belong to the app, so the sidebar shows them with this page closed.
        services.Timer.PropertyChanged += TimerChanged;
        Period = PeriodChoices[0];
        Refresh();
    }

    private void TimerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(e.PropertyName);

    /// <summary>Let go when the page is: the app's timer outlives it.</summary>
    public void Dispose()
    {
        services.Timer.PropertyChanged -= TimerChanged;
        GC.SuppressFinalize(this);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private DateOnly choicesMadeFor;

    /// <summary>
    /// Builds Today, Yesterday and the months again once the date has moved on, as it does when Webignite stays
    /// open past midnight. "Today" stays today; a day picked by date stays that date.
    /// </summary>
    private void CatchUpDays()
    {
        var today = Today;
        if (today == choicesMadeFor)
            return;
        var wasToday = Day is null || Day.Value == choicesMadeFor;
        var keep = Day?.Value;
        var invoiceLabel = InvoiceMonth?.Label;
        choicesMadeFor = today;

        DayChoices = Enumerable.Range(0, 14).Select(back => today.AddDays(-back))
            .Select(day => new Choice<DateOnly>(ClientRecords.Day(day, today), day))
            .ToList();
        OnPropertyChanged(nameof(DayChoices));
        Day = wasToday ? DayChoices[0] : DayChoices.FirstOrDefault(c => c.Value == keep) ?? DayChoices[0];

        var month = Dates.MonthStart(today);
        InvoiceMonths = [new("This month", month), new("Last month", month.AddMonths(-1))];
        OnPropertyChanged(nameof(InvoiceMonths));
        InvoiceMonth = InvoiceMonths.FirstOrDefault(c => c.Label == invoiceLabel) ?? InvoiceMonths[0];
    }

    /// <summary>Who time is for: a client, or nobody (admin, learning, quoting).</summary>
    public IReadOnlyList<Choice<Client?>> ClientChoices { get; private set; } = [];
    public ObservableCollection<ProjectChoice> ProjectChoices { get; } = [];
    public IReadOnlyList<Choice<DateOnly>> DayChoices { get; private set; } = [];

    public static IReadOnlyList<Choice<TimeRules.Period>> PeriodChoices { get; } =
    [
        new("This week", TimeRules.Period.ThisWeek),
        new("Last week", TimeRules.Period.LastWeek),
        new("This month", TimeRules.Period.ThisMonth),
        new("Last month", TimeRules.Period.LastMonth),
    ];

    public ObservableCollection<TimeTotalRow> Totals { get; } = [];
    public ObservableCollection<TimeDayGroup> Days { get; } = [];

    [ObservableProperty] public partial string Note { get; set; } = "";
    [ObservableProperty] public partial Choice<Client?>? ClientChoice { get; set; }
    [ObservableProperty] public partial ProjectChoice? ProjectChoice { get; set; }
    [ObservableProperty] public partial string Hours { get; set; } = "";
    [ObservableProperty] public partial Choice<DateOnly> Day { get; set; }
    [ObservableProperty] public partial Choice<TimeRules.Period> Period { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool MessageIsProblem { get; set; }
    [ObservableProperty] public partial string PeriodTotal { get; set; } = "";

    /// <summary>"R 8 125.00 at your hourly rates", or empty when no client has a rate.</summary>
    [ObservableProperty] public partial string PeriodValue { get; set; } = "";
    public string Clock => services.Timer.Clock;

    /// <summary>The timer that's running, if any. It survives closing the app.</summary>
    public RunningTimer? Running => services.Timer.Running;

    public bool IsRunning => services.Timer.IsRunning;

    /// <summary>"Karoo Coffee Roasters: New website" for the running timer, shown in the sidebar too.</summary>
    public string RunningFor => services.Timer.RunningFor;

    public bool HasProjects => ProjectChoices.Count > 1;
    public bool IsEmpty => Days.Count == 0;
    public bool HasMessage => Message.Length > 0;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnClientChoiceChanged(Choice<Client?>? value)
    {
        ProjectChoices.Clear();
        ProjectChoices.Add(new ProjectChoice("No project", null));
        foreach (var project in value?.Value?.Projects.Where(p => !p.Finished) ?? [])
            ProjectChoices.Add(new ProjectChoice(project.Name, project));
        ProjectChoice = ProjectChoices[0];
        OnPropertyChanged(nameof(HasProjects));
    }

    partial void OnPeriodChanged(Choice<TimeRules.Period> value) => Refresh();

    [RelayCommand]
    private void Start()
    {
        if (Running is not null)
            return;
        services.Timer.Start(new RunningTimer
        {
            Started = DateTimeOffset.Now,
            Note = Note.Trim(),
            ClientId = ClientChoice?.Value?.Id,
            ProjectId = ProjectChoice?.Project?.Id,
        });
        Say("");
    }

    [RelayCommand]
    private void Stop()
    {
        if (Running is null)
            return;
        var entry = services.Timer.StopAndKeep(Note.Trim());
        if (entry is null)
        {
            Say("Stopped within half a minute, so nothing was kept.");
            return;
        }
        Note = "";
        Say($"Kept {TimeRules.Format(entry.Minutes)} for {Describe(entry.ClientId, entry.ProjectId)}.");
        Refresh();
    }

    /// <summary>Adds time typed in by hand, e.g. "1:30" or "1.5" on yesterday.</summary>
    [RelayCommand]
    private void Add()
    {
        CatchUpDays();
        if (TimeRules.ParseMinutes(Hours) is not { } minutes)
        {
            Say("Type how long, like 1.5, 1:30 or 45m. Up to 16 hours at a time.", problem: true);
            return;
        }
        var entry = services.Store.SaveTime(new TimeEntry
        {
            Id = TimeEntry.NewId(),
            Date = Day.Value,
            Minutes = minutes,
            Note = Note.Trim(),
            ClientId = ClientChoice?.Value?.Id,
            ProjectId = ClientChoice?.Value is null ? null : ProjectChoice?.Project?.Id,
            Created = DateTimeOffset.Now,
        });
        Hours = "";
        Note = "";
        Say($"Added {TimeRules.Format(entry.Minutes)} for {Describe(entry.ClientId, entry.ProjectId)}.");
        Refresh();
    }

    /// <summary>How much the app moves, for the fade on a removed row.</summary>
    internal Motion Motion => services.Settings.Appearance.Motion;

    public void Delete(TimeEntry entry)
    {
        services.Store.DeleteTime(entry.Id);
        Say($"Removed {TimeRules.Format(entry.Minutes)} from {entry.Date:d MMM}.");
        Refresh();
        services.OfferUndo($"Removed {TimeRules.Format(entry.Minutes)}", () =>
        {
            services.Store.SaveTime(entry);
            Say("");
            Refresh();
        });
    }

    private void Say(string text, bool problem = false)
    {
        Message = text;
        MessageIsProblem = problem;
    }

    private string Describe(string? clientId, string? projectId) =>
        TimeRules.Describe(new TimeEntry { Id = "", Date = Today, Minutes = 0, ClientId = clientId, ProjectId = projectId }, clients);

    private (DateOnly From, DateOnly To) Range() => TimeRules.Range(Period?.Value ?? TimeRules.Period.ThisWeek, Today);

    public void Refresh()
    {
        CatchUpDays();
        clients = services.Clients.List();
        ClientChoices = ClientPicker.Choices(TimeRules.NoClient, clients);
        OnPropertyChanged(nameof(ClientChoices));
        ClientChoice = ClientPicker.Keep(ClientChoices, ClientChoice);
        LoadInvoiceChoices(clients);

        var (from, to) = Range();
        var entries = services.Store.ListTime(from, to);
        var total = entries.Sum(e => e.Minutes);
        PeriodTotal = TimeRules.Format(total);
        var (worth, unpriced) = TimeRules.Worth(entries, clients);
        PeriodValue = worth is not { } value ? ""
            : unpriced == 0 ? $"{Money.Rand(value)} at your hourly rates"
            : $"{Money.Rand(value)} at your hourly rates, and {TimeRules.Format(unpriced)} with no rate";
        Totals.Clear();
        foreach (var line in TimeRules.Totals(entries, clients))
            Totals.Add(new TimeTotalRow(line, total));
        Days.Clear();
        foreach (var day in entries.GroupBy(e => e.Date))
            Days.Add(new TimeDayGroup(
                day.Key == Today ? "Today" : day.Key == Today.AddDays(-1) ? "Yesterday" : day.Key.ToString("dddd d MMMM"),
                TimeRules.Format(day.Sum(e => e.Minutes)),
                day.Select(e => new TimeEntryRow(e, TimeRules.Describe(e, clients), this)).ToList()));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(RunningFor));
    }
}

/// <summary>A line in the totals: a client, a project under it, or time not for a client, with a bar for its share.</summary>
public sealed record TimeTotalRow(TimeTotal Total, int PeriodMinutes)
{
    public string Label => Total.Label;
    public string Time => TimeRules.Format(Total.Minutes);
    public bool IsProject => Total.ProjectId is not null;
    public string Value => Total.Value is { } value ? Money.Rand(value) : "";

    /// <summary>Share of the period's time, 0 to 1, for the bar.</summary>
    public double Share => PeriodMinutes == 0 ? 0 : (double)Total.Minutes / PeriodMinutes;

    /// <summary>The bar's width in pixels, out of 160.</summary>
    public double BarWidth => Math.Max(2, Share * 160);
}

public sealed record TimeDayGroup(string Title, string Total, IReadOnlyList<TimeEntryRow> Entries);

public sealed partial class TimeEntryRow(TimeEntry entry, string who, TimeViewModel owner) : ViewModelBase
{
    public string Who => who;
    public string Note => entry.Note;
    public bool HasNote => entry.Note.Length > 0;
    public string Time => TimeRules.Format(entry.Minutes);

    /// <summary>On its way out after Delete: the row fades before it is removed.</summary>
    [ObservableProperty]
    public partial bool IsLeaving { get; set; }

    [RelayCommand]
    private void Delete() => Feedback.Leave(owner.Motion, () => IsLeaving = true, () => owner.Delete(entry));
}

/// <summary>Projects sit indented under their client in the totals, with a lighter bar.</summary>
public sealed class TimeIndent : Avalonia.Data.Converters.IValueConverter
{
    public static readonly TimeIndent Instance = new();
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, double> BarOpacity = new(isProject => isProject ? 0.45 : 1);

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? new Avalonia.Thickness(18, 0, 0, 0) : new Avalonia.Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
