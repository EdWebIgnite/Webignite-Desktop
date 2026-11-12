using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

// Beside the dock: small things the person picks for the space left and right of it (the day, a clock, the timer,
// chat, search, a quick note). Which ones, on which side and in what order are theirs to choose (DockBar in Core);
// so is whether the dock has a strip of its own under the page or floats over the bottom of it.
public sealed partial class MainViewModel
{
    public ObservableCollection<BarItem> BarLeft { get; } = [];
    public ObservableCollection<BarItem> BarRight { get; } = [];

    /// <summary>What isn't beside the dock yet, for the Add list.</summary>
    public ObservableCollection<BarItem> BarAvailable { get; } = [];

    public bool HasBarLeft => BarLeft.Count > 0;
    public bool HasBarRight => BarRight.Count > 0;
    public bool HasBarAvailable => BarAvailable.Count > 0;

    /// <summary>The dock floats over the bottom of the page; off, it has a strip of its own under it.</summary>
    public bool DockFloats => services.Settings.Appearance.DockFloats;

    /// <summary>The time and the day as the PC's region writes them. Kept up by the dock while it is on screen.</summary>
    [ObservableProperty] public partial string ClockText { get; private set; } = "";
    [ObservableProperty] public partial string TodayText { get; private set; } = "";

    private BarLayout Bar => DockBar.Shown(services.Settings.Appearance.BarLeft, services.Settings.Appearance.BarRight);

    private void FillBar()
    {
        BarLeft.Clear();
        BarRight.Clear();
        BarAvailable.Clear();
        if (IsDockMenu)
        {
            var bar = Bar;
            foreach (var widget in bar.Left)
                BarLeft.Add(new BarItem(widget));
            foreach (var widget in bar.Right)
                BarRight.Add(new BarItem(widget));
            foreach (var widget in DockBar.Available(bar))
                BarAvailable.Add(new BarItem(widget));
            TickClock();
        }
        OnPropertyChanged(nameof(HasBarLeft));
        OnPropertyChanged(nameof(HasBarRight));
        OnPropertyChanged(nameof(HasBarAvailable));
    }

    /// <summary>Reads the time again. The dock calls it a few times a minute; nothing changes unless the minute did.</summary>
    public void TickClock()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("t", CultureInfo.CurrentCulture);
        TodayText = now.ToString("ddd d MMM", CultureInfo.CurrentCulture);
    }

    private void KeepBar(BarLayout bar)
    {
        // Saving the look tells everything that follows it, the dock and this bar included.
        services.Settings.Appearance = services.Settings.Appearance with { BarLeft = DockBar.Saved(bar.Left), BarRight = DockBar.Saved(bar.Right) };
    }

    [RelayCommand]
    private void AddToBarLeft(BarItem? item)
    {
        if (item is not null)
            KeepBar(DockBar.Add(Bar, item.Widget, BarSide.Left));
    }

    [RelayCommand]
    private void AddToBarRight(BarItem? item)
    {
        if (item is not null)
            KeepBar(DockBar.Add(Bar, item.Widget, BarSide.Right));
    }

    [RelayCommand]
    private void TakeOffBar(BarItem? item)
    {
        if (item is not null)
            KeepBar(DockBar.Remove(Bar, item.Widget));
    }

    [RelayCommand]
    private void MoveBarAcross(BarItem? item)
    {
        if (item is not null)
            KeepBar(DockBar.Across(Bar, item.Widget));
    }

    /// <summary>Back to what a new install shows beside the dock.</summary>
    [RelayCommand]
    private void UsualBar() => services.Settings.Appearance = services.Settings.Appearance with { BarLeft = null, BarRight = null };

    /// <summary>Starts the timer with nothing written yet, or stops it and keeps the time; Time is where it is given a client.</summary>
    [RelayCommand]
    private void ToggleTimer()
    {
        if (Timer.IsRunning)
            Timer.StopAndKeep("");
        else
            Timer.Start(new Webignite.Core.Time.RunningTimer { Started = DateTimeOffset.Now, Note = "" });
    }

    /// <summary>Settings, Look and feel, where what sits beside the dock is chosen.</summary>
    [RelayCommand]
    private void ChooseBar() => services.Go.Settings("look");

    [RelayCommand]
    private void OpenToday() => Selected = this[Page.Home];

    [RelayCommand]
    private void QuickNote() => services.Go.QuickNote();
}

/// <summary>One thing beside the dock. The view shows the part that matches what it is.</summary>
public sealed record BarItem(BarWidget Widget)
{
    public string Name => DockBar.Name(Widget);
    public bool IsToday => Widget == BarWidget.Today;
    public bool IsClock => Widget == BarWidget.Clock;
    public bool IsTimer => Widget == BarWidget.Timer;
    public bool IsChat => Widget == BarWidget.Chat;
    public bool IsSearch => Widget == BarWidget.Search;
    public bool IsQuickNote => Widget == BarWidget.QuickNote;
}
