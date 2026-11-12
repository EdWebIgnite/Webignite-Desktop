using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The small tools from the sidebar, each in a window of its own. Clicking one that is open brings it forward;
/// they all close with the app.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<ToolKind, ToolWindow> toolWindows = [];
    // From another program: with Webignite minimised, the tool shows on its own rather than hidden with the main window.
    private void ShowClipboardFromAnywhere(AppServices services)
    {
        var window = ShowTool(ToolKind.Clipboard, services, owned: WindowState != Avalonia.Controls.WindowState.Minimized);
        window.Activate();
    }

    // From another program: the window that was in front is remembered, so the snippet picked is pasted there.
    private void ShowSnippetsFromAnywhere(AppServices services)
    {
        var typingIn = OperatingSystem.IsWindows() ? Files.PasteElsewhere.WindowInFront() : IntPtr.Zero;
        if (toolWindows.TryGetValue(ToolKind.Snippets, out var already))
            already.Close();
        var window = ShowTool(ToolKind.Snippets, services, owned: false);
        if (window.View is SnippetsView view)
            view.PasteInto = typingIn;
        window.Tools.Snippets.SnippetsPaste = typingIn != IntPtr.Zero;
        window.Topmost = true;
        window.Activate();
    }

    private void WireTools(AppServices services)
    {
        services.Go.CompareAction = (left, right) => ShowTool(ToolKind.Compare, services).Tools.Compare.StartCompare(left, right);
        services.Go.DuplicatesAction = folder => ShowTool(ToolKind.Duplicates, services).Tools.Duplicates.StartDuplicates(folder);
        services.Go.MainToFrontAction = Activate;
        OnShortcut(Webignite.Core.Tools.GlobalShortcut.Snippets, () => ShowSnippetsFromAnywhere(services));
        services.Go.OpenToolAction = kind => ShowTool(kind, services);
        // Clipboard history listens from the start, so what's copied before the tool is opened is there too.
        Opened += (_, _) => services.Clipboard.Watch(this);
        Closing += (_, _) =>
        {
            foreach (var open in toolWindows.Values.ToList())
                open.Close();
        };
        Closed += (_, _) => services.Clipboard.Stop();
        OnShortcut(Webignite.Core.Tools.GlobalShortcut.ClipboardHistory, () => ShowClipboardFromAnywhere(services));
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
