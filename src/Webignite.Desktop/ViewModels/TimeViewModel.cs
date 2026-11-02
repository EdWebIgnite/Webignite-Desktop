using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Time;

namespace Webignite.Desktop.ViewModels;

/// <summary>Who time is for: a client, or nobody (admin, learning, quoting).</summary>
public sealed record ClientChoice(string Label, Client? Client)
{
    public override string ToString() => Label;
}

/// <summary>Which project of the client, or none.</summary>
public sealed record ProjectChoice(string Label, ClientProject? Project)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Time page: a start/stop timer, time typed in by hand, totals per client and project, and the entries.
/// Time can be for a client, one of the client's projects, or nobody at all.
/// </summary>
public partial class TimeViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private IReadOnlyList<Client> clients = [];

    public TimeViewModel(AppServices services)
    {
        this.services = services;
        tick.Tick += (_, _) => UpdateClock();
        DayChoices = Enumerable.Range(0, 14).Select(back => Today.AddDays(-back))
            .Select(day => new Choice<DateOnly>(day == Today ? "Today" : day == Today.AddDays(-1) ? "Yesterday" : day.ToString("ddd d MMM"), day))
            .ToList();
        Day = DayChoices[0];
        Period = PeriodChoices[0];
        Running = services.Store.RunningTimer;
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public ObservableCollection<ClientChoice> ClientChoices { get; } = [];
    public ObservableCollection<ProjectChoice> ProjectChoices { get; } = [];
    public IReadOnlyList<Choice<DateOnly>> DayChoices { get; }

    public static IReadOnlyList<Choice<int>> PeriodChoices { get; } =
    [
        new("This week", 0),
        new("Last week", 1),
        new("This month", 2),
        new("Last month", 3),
    ];

    public ObservableCollection<TimeTotalRow> Totals { get; } = [];
    public ObservableCollection<TimeDayGroup> Days { get; } = [];

    [ObservableProperty] public partial string Note { get; set; } = "";
    [ObservableProperty] public partial ClientChoice? ClientChoice { get; set; }
    [ObservableProperty] public partial ProjectChoice? ProjectChoice { get; set; }
    [ObservableProperty] public partial string Hours { get; set; } = "";
    [ObservableProperty] public partial Choice<DateOnly> Day { get; set; }
    [ObservableProperty] public partial Choice<int> Period { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool MessageIsProblem { get; set; }
    [ObservableProperty] public partial string PeriodTotal { get; set; } = "";
    [ObservableProperty] public partial string Clock { get; set; } = "";

    /// <summary>The timer that's running, if any. It survives closing the app.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(RunningFor))]
    public partial RunningTimer? Running { get; set; }

    public bool IsRunning => Running is not null;

    /// <summary>"Karoo Coffee Roasters: New website" for the running timer, shown in the sidebar too.</summary>
    public string RunningFor => Running is null ? "" : Running.Note is { Length: > 0 } note ? note : Describe(Running.ClientId, Running.ProjectId);

    public bool HasProjects => ProjectChoices.Count > 1;
    public bool IsEmpty => Days.Count == 0;
    public bool HasMessage => Message.Length > 0;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnClientChoiceChanged(ClientChoice? value)
    {
        ProjectChoices.Clear();
        ProjectChoices.Add(new ProjectChoice("No project", null));
        foreach (var project in value?.Client?.Projects.Where(p => !p.Finished) ?? [])
            ProjectChoices.Add(new ProjectChoice(project.Name, project));
        ProjectChoice = ProjectChoices[0];
        OnPropertyChanged(nameof(HasProjects));
    }

    partial void OnPeriodChanged(Choice<int> value) => Refresh();

    partial void OnRunningChanged(RunningTimer? value)
    {
        if (value is null)
            tick.Stop();
        else
            tick.Start();
        UpdateClock();
    }

    private void UpdateClock() =>
        Clock = Running is { } running ? TimeRules.Clock(DateTimeOffset.Now - running.Started) : "0:00:00";

    [RelayCommand]
    private void Start()
    {
        if (Running is not null)
            return;
        Running = new RunningTimer
        {
            Started = DateTimeOffset.Now,
            Note = Note.Trim(),
            ClientId = ClientChoice?.Client?.Id,
            ProjectId = ProjectChoice?.Project?.Id,
        };
        services.Store.SetRunningTimer(Running);
        Say("");
    }

    [RelayCommand]
    private void Stop()
    {
        if (Running is not { } running)
            return;
        var entry = TimeRules.Stop(running with { Note = running.Note.Length > 0 ? running.Note : Note.Trim() }, DateTimeOffset.Now);
        services.Store.SetRunningTimer(null);
        Running = null;
        if (entry is null)
        {
            Say("Stopped within half a minute, so nothing was kept.");
            return;
        }
        services.Store.SaveTime(entry);
        Note = "";
        Say($"Kept {TimeRules.Format(entry.Minutes)} for {Describe(entry.ClientId, entry.ProjectId)}.");
        Refresh();
    }

    /// <summary>Adds time typed in by hand, e.g. "1:30" or "1.5" on yesterday.</summary>
    [RelayCommand]
    private void Add()
    {
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
            ClientId = ClientChoice?.Client?.Id,
            ProjectId = ClientChoice?.Client is null ? null : ProjectChoice?.Project?.Id,
            Created = DateTimeOffset.Now,
        });
        Hours = "";
        Note = "";
        Say($"Added {TimeRules.Format(entry.Minutes)} for {Describe(entry.ClientId, entry.ProjectId)}.");
        Refresh();
    }

    public void Delete(TimeEntry entry)
    {
        services.Store.DeleteTime(entry.Id);
        Say($"Removed {TimeRules.Format(entry.Minutes)} from {entry.Date:d MMM}.");
        Refresh();
    }

    private void Say(string text, bool problem = false)
    {
        Message = text;
        MessageIsProblem = problem;
    }

    private string Describe(string? clientId, string? projectId) =>
        TimeRules.Describe(new TimeEntry { Id = "", Date = Today, Minutes = 0, ClientId = clientId, ProjectId = projectId }, clients);

    private (DateOnly From, DateOnly To) Range()
    {
        var week = TimeRules.WeekStart(Today);
        var month = new DateOnly(Today.Year, Today.Month, 1);
        return Period?.Value switch
        {
            1 => (week.AddDays(-7), week.AddDays(-1)),
            2 => (month, month.AddMonths(1).AddDays(-1)),
            3 => (month.AddMonths(-1), month.AddDays(-1)),
            _ => (week, week.AddDays(6)),
        };
    }

    public void Refresh()
    {
        clients = services.Store.ListClients();
        var keepClient = ClientChoice?.Client?.Id;
        ClientChoices.Clear();
        ClientChoices.Add(new ClientChoice(TimeRules.NoClient, null));
        foreach (var client in clients)
            ClientChoices.Add(new ClientChoice(client.Name, client));
        ClientChoice = ClientChoices.FirstOrDefault(c => c.Client?.Id == keepClient) ?? ClientChoices[0];

        var (from, to) = Range();
        var entries = services.Store.ListTime(from, to);
        var total = entries.Sum(e => e.Minutes);
        PeriodTotal = TimeRules.Format(total);
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
        UpdateClock();
    }
}

/// <summary>A line in the totals: a client, a project under it, or time not for a client, with a bar for its share.</summary>
public sealed record TimeTotalRow(TimeTotal Total, int PeriodMinutes)
{
    public string Label => Total.Label;
    public string Time => TimeRules.Format(Total.Minutes);
    public bool IsProject => Total.ProjectId is not null;

    /// <summary>Share of the period's time, 0 to 1, for the bar.</summary>
    public double Share => PeriodMinutes == 0 ? 0 : (double)Total.Minutes / PeriodMinutes;

    /// <summary>The bar's width in pixels, out of 160.</summary>
    public double BarWidth => Math.Max(2, Share * 160);
}

public sealed record TimeDayGroup(string Title, string Total, IReadOnlyList<TimeEntryRow> Entries);

public sealed partial class TimeEntryRow(TimeEntry entry, string who, TimeViewModel owner)
{
    public string Who => who;
    public string Note => entry.Note;
    public bool HasNote => entry.Note.Length > 0;
    public string Time => TimeRules.Format(entry.Minutes);

    [RelayCommand]
    private void Delete() => owner.Delete(entry);
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
