using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Mail being written opens in a window of its own when that is switched on (Settings, Mail; on to start with).
/// One window per mail being written; it closes when the mail is sent, saved or discarded, and with the app.
/// </summary>
public partial class MainWindow
{
    private readonly List<ComposeWindow> composing = [];

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
            var window = new ComposeWindow(composer);
            composing.Add(window);
            window.Closed += (_, _) => composing.Remove(window);
            window.Show(this);
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
