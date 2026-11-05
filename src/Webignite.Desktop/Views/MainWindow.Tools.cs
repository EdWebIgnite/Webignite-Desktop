using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The small tools from the sidebar, each in a window of its own. Clicking one that is open brings it forward;
/// they all close with the app.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<ToolKind, ToolWindow> toolWindows = [];

    private void WireTools(AppServices services)
    {
        services.Go.OpenToolAction = kind => ShowTool(kind, services);
        // Clipboard history listens from the start, so what's copied before the tool is opened is there too.
        Opened += (_, _) => services.Clipboard.Watch(this);
        Closing += (_, _) =>
        {
            foreach (var open in toolWindows.Values.ToList())
                open.Close();
        };
        Closed += (_, _) => services.Clipboard.Stop();
    }

    /// <summary>Opens a tool, or brings its window forward. Returns the window, for the screenshot tool.</summary>
    public ToolWindow ShowTool(ToolKind kind, AppServices services)
    {
        if (toolWindows.TryGetValue(kind, out var open))
        {
            open.Activate();
            return open;
        }
        var window = new ToolWindow(kind, new ToolsViewModel(services));
        window.Closed += (_, _) => toolWindows.Remove(kind);
        toolWindows[kind] = window;
        window.Show(this);
        return window;
    }
}
