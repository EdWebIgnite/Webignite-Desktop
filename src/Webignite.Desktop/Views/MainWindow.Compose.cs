using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Mail being written opens in a window of its own when that is switched on (Settings, Mail; on to start with).
/// One window per mail being written; it closes when the mail is sent, saved or discarded, and with the app.
/// </summary>
public partial class MainWindow
{
    private readonly List<ComposeWindow> composing = [];

    /// <summary>The mails being written in windows of their own, for the screenshot tool to find.</summary>
    public IReadOnlyList<ComposeWindow> ComposeWindows => composing;

    private void WireCompose(AppServices services)
    {
        services.ShowComposer = composer =>
        {
            if (!services.Settings.ComposeInWindow)
                return false;
            if (composing.FirstOrDefault(w => w.Composer == composer) is { } open)
            {
                open.Activate();
                return true;
            }
            // A window of its own, not owned by this one: on Windows an owned window didn't lay itself out again when
            // maximised or dragged bigger (the new area showed what was behind it), and a mail written on its own
            // should be able to go full screen and have its own place on the taskbar, as a popped-out page does.
            var window = new ComposeWindow(composer) { Icon = Icon };
            if (WindowState == Avalonia.Controls.WindowState.Normal || WindowState == Avalonia.Controls.WindowState.Maximized)
                window.Position = new Avalonia.PixelPoint(Position.X + 80, Position.Y + 60);
            composing.Add(window);
            window.Closed += (_, _) => composing.Remove(window);
            window.Show();
            window.Activate();
            return true;
        };
        services.HideComposer = composer => composing.FirstOrDefault(w => w.Composer == composer)?.Leave();
        Closed += (_, _) =>
        {
            foreach (var window in composing.ToList())
                window.Leave();
        };
    }
}
