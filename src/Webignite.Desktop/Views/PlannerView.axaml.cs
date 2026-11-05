using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Webignite.Core.Layout;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class PlannerView : UserControl
{
    private double? startHeight;
    private Point startPoint;

    public PlannerView() => InitializeComponent();

    private PlannerViewModel? Planner => DataContext as PlannerViewModel;

    // The handle under the week: drag to set how tall the days are, saved on letting go.
    private void GripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount > 1)
            return;
        startHeight = DayRow.Bounds.Height;
        startPoint = e.GetPosition(this);
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void GripMoved(object? sender, PointerEventArgs e)
    {
        if (startHeight is not { } start || Planner is not { } planner)
            return;
        planner.DayHeight = CalendarSizing.PlannerDay(start + e.GetPosition(this).Y - startPoint.Y);
    }

    private void GripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (startHeight is null || Planner is not { } planner)
            return;
        startHeight = null;
        e.Pointer.Capture(null);
        planner.KeepDayHeight(planner.DayHeight);
    }

    private void GripReset(object? sender, TappedEventArgs e) => Planner?.KeepDayHeight(null);
}
