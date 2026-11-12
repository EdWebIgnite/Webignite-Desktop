using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The diary: two pages open like a book, a day to a page.</summary>
public partial class DiaryView : UserControl
{
    // How long each half of a page turn takes: the page lifting to the spine, then coming down on the other side.
    private static readonly TimeSpan HalfTurn = TimeSpan.FromMilliseconds(190);

    private bool turning;

    public DiaryView() => InitializeComponent();

    private DiaryViewModel? Diary => DataContext as DiaryViewModel;

    // Notes are kept as soon as the caret leaves them, and again when the page is turned or the app closes.
    private void KeepNotes(object? sender, RoutedEventArgs e) => Diary?.Keep();

    private void TurnBack(object? sender, RoutedEventArgs e) => TurnAsync(-1).Forget("Turning the diary back a page");

    private void TurnOn(object? sender, RoutedEventArgs e) => TurnAsync(1).Forget("Turning the diary's page");

    private void TurnToToday(object? sender, RoutedEventArgs e)
    {
        if (Diary is not { } diary)
            return;
        var today = Webignite.Core.Calendar.Diary.Spread(DateOnly.FromDateTime(DateTime.Today)).Left;
        TurnAsync(today.CompareTo(diary.Left.Date), toToday: true).Forget("Opening the diary at today");
    }

    // Shows the next two pages (1), the last two (-1) or today's (0 when already there). With full motion the page
    // turns: the one being left folds up to the spine and the new one comes down on the other side. With reduced
    // motion or none, the pages simply change.
    /// <summary>Turns the page: 1 forward, -1 back. Done when the page has settled.</summary>
    public Task Turn(int direction) => TurnAsync(direction);

    private async Task TurnAsync(int direction, bool toToday = false)
    {
        if (Diary is not { } diary || turning)
            return;
        // "Today" from somewhere else turns one page the right way and lands on today's.
        void Change()
        {
            if (toToday)
                diary.GoToTodayCommand.Execute(null);
            else if (direction > 0)
                diary.NextPagesCommand.Execute(null);
            else
                diary.PreviousPagesCommand.Execute(null);
        }
        var top = TopLevel.GetTopLevel(this);
        if (direction == 0 || top?.Classes.Contains("motion-full") != true || LeftPage.Bounds.Width < 1 || RightPage.Bounds.Width < 1)
        {
            if (direction != 0 || toToday)
                Change();
            return;
        }
        turning = true;
        RenderTargetBitmap? left = null, right = null;
        try
        {
            left = Picture(LeftPage, top.RenderScaling);
            right = Picture(RightPage, top.RenderScaling);
            (OldLeft.Source, OldRight.Source) = (left, right);
            OldLeft.IsVisible = OldRight.IsVisible = true;
            // Forward, the right-hand page folds over to the left; back, the left-hand page folds over to the right.
            var (leaving, arriving, under) = direction > 0 ? ((Control)OldRight, (Control)LeftPage, (Control)OldLeft) : (OldLeft, RightPage, OldRight);
            var hinge = direction > 0 ? 0.0 : 1.0; // the spine's side of the page that is leaving
            leaving.ZIndex = 2;
            under.ZIndex = 0;
            LeftPage.ZIndex = RightPage.ZIndex = 1;
            var lift = new ScaleTransform(1, 1);
            var land = new ScaleTransform(0, 1);
            leaving.RenderTransformOrigin = new RelativePoint(hinge, 0.5, RelativeUnit.Relative);
            leaving.RenderTransform = lift;
            arriving.RenderTransformOrigin = new RelativePoint(1 - hinge, 0.5, RelativeUnit.Relative);
            arriving.RenderTransform = land;

            Change();

            // Up to the spine, quickening, and darkening as it turns away from the light.
            await Tween(HalfTurn, t =>
            {
                lift.ScaleX = 1 - t * t;
                leaving.Opacity = 1 - 0.3 * t;
            });
            leaving.IsVisible = false;
            // Down on the other side, slowing as it settles.
            await Tween(HalfTurn, t =>
            {
                land.ScaleX = 1 - (1 - t) * (1 - t);
                arriving.Opacity = 0.7 + 0.3 * t;
            });
        }
        finally
        {
            foreach (var control in new Control[] { OldLeft, OldRight, LeftPage, RightPage })
            {
                control.RenderTransform = null;
                control.Opacity = 1;
                control.ZIndex = 0;
            }
            OldLeft.IsVisible = OldRight.IsVisible = false;
            OldLeft.Source = OldRight.Source = null;
            left?.Dispose();
            right?.Dispose();
            turning = false;
        }
    }

    private static RenderTargetBitmap Picture(Control page, double scaling)
    {
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(page.Bounds.Width * scaling)), Math.Max(1, (int)Math.Ceiling(page.Bounds.Height * scaling)));
        var picture = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        picture.Render(page);
        return picture;
    }

    // Calls step with 0 to 1 over the time given, once a frame.
    private static async Task Tween(TimeSpan time, Action<double> step)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var t = Math.Min(1, watch.Elapsed / time);
            step(t);
            if (t >= 1)
                return;
            await Task.Delay(15);
        }
    }
}
