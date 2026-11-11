using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The small tools from the sidebar, each in a window of its own. Clicking one that is open brings it forward;
/// they all close with the app.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<ToolKind, ToolWindow> toolWindows = [];
    private Files.GlobalHotkey? clipboardKey;

    private string? ApplyClipboardShortcut(AppServices services)
    {
        if (!OperatingSystem.IsWindows() || clipboardKey is null)
            return null;
        var shortcut = services.Settings.Screenshots.ClipboardShortcut;
        if (string.IsNullOrWhiteSpace(shortcut))
            return clipboardKey.Set(null);
        var (keys, problem) = Webignite.Core.Tools.Hotkey.Parse(shortcut);
        return problem ?? clipboardKey.Set(keys);
    }

    // From another program: with Webignite minimised, the tool shows on its own rather than hidden with the main window.
    private void ShowClipboardFromAnywhere(AppServices services)
    {
        var window = ShowTool(ToolKind.Clipboard, services, owned: WindowState != Avalonia.Controls.WindowState.Minimized);
        window.Activate();
    }

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
        services.ApplyClipboardShortcut = () => ApplyClipboardShortcut(services);
        Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows())
                return;
            clipboardKey = new Files.GlobalHotkey(this, 0x574B, () => ShowClipboardFromAnywhere(services), "Screenshots");
            if (ApplyClipboardShortcut(services) is { } problem)
                ShowNote("Clipboard history shortcut not set", problem, () => services.Go.Settings("screenshots"));
        };
        Closed += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                clipboardKey?.Release();
            clipboardKey = null;
        };
    }

    /// <summary>Opens a tool, or brings its window forward. Returns the window, for the screenshot tool.</summary>
    public ToolWindow ShowTool(ToolKind kind, AppServices services, bool owned = true)
    {
        if (toolWindows.TryGetValue(kind, out var open))
        {
            open.Activate();
            return open;
        }
        var window = new ToolWindow(kind, new ToolsViewModel(services));
        window.Closed += (_, _) => toolWindows.Remove(kind);
        toolWindows[kind] = window;
        // A tool that fills a big window stands on its own, so this window can come in front of it again
        // (to show what it saved, say). The small ones float over this window.
        if (owned && !window.Fills)
            window.Show(this);
        else
            window.Show();
        return window;
    }
}
