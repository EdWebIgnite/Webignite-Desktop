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
    // How long a page takes to turn, from lying flat on one side to lying flat on the other.
    private static readonly TimeSpan TurnTime = TimeSpan.FromMilliseconds(850);

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
    /// <summary>
    /// Turns the page: 1 forward, -1 back. Done when the page has settled. With <paramref name="holdAt"/> (0 to 1)
    /// the page stops that far through its turn until <see cref="LetGo"/>, which is how the screenshot tool draws it.
    /// </summary>
    public Task Turn(int direction, double? holdAt = null) => TurnAsync(direction, holdAt: holdAt);

    /// <summary>Lets a page held mid-turn settle.</summary>
    public void LetGo() => held?.TrySetResult();

    private TaskCompletionSource? held;

    private async Task TurnAsync(int direction, bool toToday = false, double? holdAt = null)
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
            // Forward, the right-hand page turns over to the left; back, the left-hand page turns over to the right.
            var forward = direction > 0;
            var (leaving, arriving, under) = forward ? ((Control)OldRight, (Control)LeftPage, (Control)OldLeft) : (OldLeft, RightPage, OldRight);
            var (leavingShade, arrivingShade) = forward ? (ShadeRight, ShadeLeft) : (ShadeLeft, ShadeRight);
            var hinge = forward ? 0.0 : 1.0; // the spine's side of the page that is leaving
            under.ZIndex = 0;
            LeftPage.ZIndex = RightPage.ZIndex = 1;
            leaving.ZIndex = 2;
            leavingShade.ZIndex = arrivingShade.ZIndex = 3;

            // The page swings about the spine like a real one: seen from the front, a page at an angle is as wide
            // as the cosine of that angle, its free edge comes towards you (a little taller, tilted), and it is in
            // its own shadow most when it stands upright.
            Hinge(leaving, leavingShade, hinge);
            Hinge(arriving, arrivingShade, 1 - hinge);
            Swing(arriving, arrivingShade, 0, 1, 0);
            leavingShade.Background = Shadow(fromLeft: forward);
            arrivingShade.Background = Shadow(fromLeft: !forward);
            leavingShade.Opacity = arrivingShade.Opacity = 0;
            leavingShade.IsVisible = arrivingShade.IsVisible = true;

            Change();

            void Pose(double t)
            {
                // Slow to lift, quick through the middle, slow to settle.
                var eased = t * t * (3 - 2 * t);
                var angle = eased * Math.PI;
                var upright = Math.Sin(angle);
                var side = forward ? -1 : 1;
                if (angle < Math.PI / 2)
                {
                    Swing(leaving, leavingShade, Math.Cos(angle), 1 + 0.035 * upright, side * 5 * upright);
                    leavingShade.Opacity = 0.55 * upright;
                }
                else
                {
                    if (leaving.IsVisible)
                        leaving.IsVisible = leavingShade.IsVisible = false;
                    Swing(arriving, arrivingShade, -Math.Cos(angle), 1 + 0.035 * upright, -side * 5 * upright);
                    arrivingShade.Opacity = 0.55 * upright;
                }
            }

            if (holdAt is { } stop)
            {
                Pose(Math.Clamp(stop, 0, 1));
                held = new TaskCompletionSource();
                await held.Task;
            }
            else
            {
                await Tween(TurnTime, Pose);
            }
        }
        finally
        {
            foreach (var control in new Control[] { OldLeft, OldRight, LeftPage, RightPage, ShadeLeft, ShadeRight })
            {
                control.RenderTransform = null;
                control.Opacity = 1;
                control.ZIndex = 0;
            }
            OldLeft.IsVisible = OldRight.IsVisible = ShadeLeft.IsVisible = ShadeRight.IsVisible = false;
            ShadeLeft.Background = ShadeRight.Background = null;
            OldLeft.Source = OldRight.Source = null;
            left?.Dispose();
            right?.Dispose();
            held = null;
            turning = false;
        }
    }

    // A page and its shadow swing together about the spine.
    private static void Hinge(Control page, Control shade, double spine)
    {
        page.RenderTransformOrigin = shade.RenderTransformOrigin = new RelativePoint(spine, 0.5, RelativeUnit.Relative);
    }

    // How the page is drawn at one moment of its swing: how wide and tall it looks, and how far its free edge tilts.
    private static void Swing(Control page, Control shade, double wide, double tall, double tiltDegrees)
    {
        var drawn = Matrix.CreateScale(wide, tall) * Matrix.CreateSkew(0, tiltDegrees * Math.PI / 180);
        page.RenderTransform = new MatrixTransform(drawn);
        shade.RenderTransform = new MatrixTransform(drawn);
    }

    // The shadow on a turning page: the paper's own ink colour, deepest at the spine and gone by the free edge.
    private LinearGradientBrush? Shadow(bool fromLeft)
    {
        if (!this.TryFindResource("PaperText", ActualThemeVariant, out var found) || found is not ISolidColorBrush ink)
            return null;
        var (near, far) = (new Color(150, ink.Color.R, ink.Color.G, ink.Color.B), new Color(20, ink.Color.R, ink.Color.G, ink.Color.B));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(fromLeft ? 0 : 1, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(fromLeft ? 1 : 0, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(near, 0), new GradientStop(far, 1) },
        };
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
