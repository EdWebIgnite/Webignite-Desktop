using Webignite.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Views;

/// <summary>
/// While recording: a small bar beside the area with the time and Stop, and a thin red line round the area
/// (four strips just outside it, so nothing covers what is being recorded and clicks still go through).
/// </summary>
public sealed class RecordingBar : Window
{
    private readonly DispatcherTimer tick;
    private readonly TextBlock time;
    private readonly List<Window> edges = [];

    public RecordingBar(Func<TimeSpan> elapsed, Action stop, bool sound)
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brush("Nav", Colors.Black);
        time = new TextBlock { Text = "0:00", Foreground = Brush("NavText", Colors.White), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 44 };
        var stopButton = new Button { Content = "Stop", Classes = { "quiet" }, Foreground = Brush("NavText", Colors.White), VerticalAlignment = VerticalAlignment.Center };
        stopButton.Click += (_, _) => stop();
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(12, 6),
            Children =
            {
                new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Fill = Brush("Attention", Colors.Red), VerticalAlignment = VerticalAlignment.Center },
                time,
                new TextBlock { Text = sound ? "Microphone on" : "No sound", Foreground = Brush("NavMuted", Colors.Gray), VerticalAlignment = VerticalAlignment.Center }.Sized("Text12"),
                stopButton,
            },
        };
        time.Text = Recordings.Clock(elapsed());
        tick = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => time.Text = Recordings.Clock(elapsed()));
        tick.Start();
    }

    public string TimeText => time.Text ?? "";

    /// <summary>Shows the bar above the area (below when there's no room, or inside when it fills the screen) and the red edges.</summary>
    public void ShowBeside(PixelRect area, PixelRect screen, double scaling)
    {
        Show();
        var size = new PixelSize((int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling));
        var gap = (int)(8 * scaling);
        var y = area.Y - size.Height - gap >= screen.Y ? area.Y - size.Height - gap
            : area.Bottom + gap + size.Height <= screen.Bottom ? area.Bottom + gap
            : area.Bottom - size.Height - gap;
        var x = Math.Clamp(area.Right - size.Width, screen.X, Math.Max(screen.X, screen.Right - size.Width));
        Position = new PixelPoint(x, y);
        var thick = Math.Max(2, (int)(2 * scaling));
        foreach (var edge in new[]
        {
            new PixelRect(area.X - thick, area.Y - thick, area.Width + thick * 2, thick),
            new PixelRect(area.X - thick, area.Bottom, area.Width + thick * 2, thick),
            new PixelRect(area.X - thick, area.Y, thick, area.Height),
            new PixelRect(area.Right, area.Y, thick, area.Height),
        })
        {
            var window = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Background = Brush("Attention", Colors.Red),
                Width = edge.Width / scaling,
                Height = edge.Height / scaling,
                Position = edge.Position,
                ShowActivated = false,
            };
            window.Show();
            edges.Add(window);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        tick.Stop();
        foreach (var edge in edges)
            edge.Close();
        edges.Clear();
        base.OnClosed(e);
    }

    private static IBrush Brush(string key, Color fallback) =>
        Application.Current?.TryGetResource(key, null, out var found) == true && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
