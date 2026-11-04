using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Time;

namespace Webignite.Desktop;

/// <summary>
/// The time-tracking timer that is running, if any, and its clock. It is kept in the database, so it survives
/// closing the app, and lives here rather than on the Time page so the sidebar can show it without that page
/// being open. The clock only ticks while a timer runs.
/// </summary>
public sealed partial class TimerClock : ObservableObject
{
    private readonly AppServices services;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public TimerClock(AppServices services)
    {
        this.services = services;
        tick.Tick += (_, _) => UpdateClock();
        Running = services.Store.RunningTimer;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(RunningFor))]
    public partial RunningTimer? Running { get; private set; }

    [ObservableProperty] public partial string Clock { get; private set; } = "0:00:00";

    public bool IsRunning => Running is not null;

    /// <summary>"Karoo Coffee Roasters: New website" for the running timer, or its note.</summary>
    public string RunningFor => Running is not { } running ? ""
        : running.Note is { Length: > 0 } note ? note
        : TimeRules.Describe(new TimeEntry { Id = "", Date = DateOnly.FromDateTime(DateTime.Today), Minutes = 0, ClientId = running.ClientId, ProjectId = running.ProjectId },
            services.Clients.List());

    public void Start(RunningTimer timer)
    {
        services.Store.SetRunningTimer(timer);
        Running = timer;
    }

    public void Stop()
    {
        services.Store.SetRunningTimer(null);
        Running = null;
    }

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
}
