using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Webignite.Core.Layout;
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

    private double? startHeight;
    private Point startPoint;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Watch(DataContext as CalendarViewModel);
        if (DataContext is CalendarViewModel calendar)
            SetMonthHeight(calendar.MonthHeight);
    }

    /// <summary>The month at a dragged height, or filling the window when there is none.</summary>
    private void SetMonthHeight(double? height)
    {
        MonthArea.RowDefinitions[0].Height = height is { } h ? new GridLength(h) : new GridLength(1, GridUnitType.Star);
        MonthArea.VerticalAlignment = height is null ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Top;
    }

    // The handle under the month: drag to set its height, saved on letting go.
    private void GripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount > 1)
            return;
        startHeight = MonthArea.RowDefinitions[0].ActualHeight;
        startPoint = e.GetPosition(this);
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void GripMoved(object? sender, PointerEventArgs e)
    {
        if (startHeight is not { } start)
            return;
        SetMonthHeight(CalendarSizing.Month(start + e.GetPosition(this).Y - startPoint.Y, MonthRoom()));
    }

    private void GripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (startHeight is null)
            return;
        startHeight = null;
        e.Pointer.Capture(null);
        (DataContext as CalendarViewModel)?.KeepMonthHeight(MonthArea.RowDefinitions[0].ActualHeight);
    }

    private void GripReset(object? sender, TappedEventArgs e)
    {
        SetMonthHeight(null);
        (DataContext as CalendarViewModel)?.KeepMonthHeight(null);
    }

    // The room the month can take: the page's own row, less the handle.
    private double MonthRoom() => Math.Max(0, Layout.RowDefinitions[1].ActualHeight - MonthArea.RowDefinitions[1].ActualHeight);

    // The divider between the month and the day panel: its new width is saved.
    private void SideDragged(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        var width = CalendarSizing.Side(Layout.ColumnDefinitions[2].ActualWidth, Layout.Bounds.Width);
        Layout.ColumnDefinitions[2].Width = new GridLength(width);
        (DataContext as CalendarViewModel)?.KeepSideWidth(width);
    }

    private CalendarViewModel? watched;

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

    /// <summary>The day panel at the width it was dragged to; otherwise narrower on a small window, giving the month more room.</summary>
    private void Narrow()
    {
        var side = DataContext is CalendarViewModel { SideWidth: { } saved }
            ? CalendarSizing.Side(saved, Layout.Bounds.Width)
            : Layout.Bounds.Width < 1000 ? 240 : 300;
        if (Layout.ColumnDefinitions[2].Width.Value != side)
            Layout.ColumnDefinitions[2].Width = new GridLength(side);
    }
}
