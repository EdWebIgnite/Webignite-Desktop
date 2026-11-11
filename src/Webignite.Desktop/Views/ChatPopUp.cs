using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Webignite.Desktop.Views;

/// <summary>
/// A new chat message, shown for a few seconds in the bottom-right corner of the screen, above other programs and
/// even while Webignite is minimised: who it's from and the start of it. A click opens the chat. It doesn't take the
/// keyboard from whatever is being typed in.
/// </summary>
public sealed class ChatPopUp : Window
{
    private readonly DispatcherTimer close;
    private readonly TextBlock heading;
    private readonly TextBlock words;

    public ChatPopUp(Action open)
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brush("Surface", Colors.White);
        heading = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("Text", Colors.Black) };
        words = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("Text", Colors.Black) };
        Content = new Border
        {
            BorderBrush = Brush("Line", Colors.LightGray),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    heading,
                    words,
                    new TextBlock { Text = "Click to open the chat", FontSize = 11, Foreground = Brush("Muted", Colors.Gray), Margin = new Thickness(0, 4, 0, 0) },
                },
            },
        };
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, _) =>
        {
            Close();
            open();
        };
        close = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) => Close());
        Closed += (_, _) => close.Stop();
    }

    /// <summary>Shows (or updates) the pop-up in the corner of the main screen and starts its time again.</summary>
    public void Say(string from, string text)
    {
        heading.Text = from;
        words.Text = text;
        if (!IsVisible)
            Show();
        if (Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            var margin = (int)(16 * scale);
            Position = new PixelPoint(area.Right - (int)(Bounds.Width * scale) - margin, area.Bottom - (int)(Bounds.Height * scale) - margin);
        }
        close.Stop();
        close.Start();
    }

    private static IBrush Brush(string key, Color fallback) =>
        Application.Current?.TryGetResource(key, null, out var found) == true && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
