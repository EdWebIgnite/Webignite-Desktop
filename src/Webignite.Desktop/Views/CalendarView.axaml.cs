using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class CalendarView : FullBleedView
{
    private const double LeastDay = 84;
    private const double DayTop = 35;
    private const double ItemHeight = 19;

    public CalendarView()
    {
        InitializeComponent();
        MonthScroller.SizeChanged += (_, _) => Fit();
        Layout.SizeChanged += (_, _) => Narrow();
    }

    private CalendarViewModel? watched;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Watch(DataContext as CalendarViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(DataContext as CalendarViewModel);
    }

    private void Watch(CalendarViewModel? calendar)
    {
        if (watched is not null)
            watched.PropertyChanged -= MonthChanged;
        watched = calendar;
        if (watched is not null)
            watched.PropertyChanged += MonthChanged;
    }

    // A new month can have five weeks or six, which changes the room a day has.
    private void MonthChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarViewModel.MonthTitle))
            Fit();
    }

    /// <summary>Tells the month how many items a day has room for at this size.</summary>
    private void Fit()
    {
        if (DataContext is not CalendarViewModel { Weeks: > 0 } calendar || MonthScroller.Bounds.Height <= 0)
            return;
        var day = Math.Max(LeastDay, MonthScroller.Bounds.Height / calendar.Weeks);
        calendar.FitPerDay((int)Math.Floor((day - DayTop) / ItemHeight));
    }

    /// <summary>On a small window the day's side column gives the month more room.</summary>
    private void Narrow()
    {
        var side = Layout.Bounds.Width < 1000 ? 240 : 300;
        if (Layout.ColumnDefinitions[2].Width.Value != side)
            Layout.ColumnDefinitions[2].Width = new GridLength(side);
    }
}
