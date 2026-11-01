namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One entry in the sidebar. The app is a shell of modules, so Mail and later tools slot in as
/// another entry without touching the rest.
/// </summary>
public sealed record Module(string Title, ViewModelBase Page, bool IsLater = false, bool FullBleed = false)
{
    /// <summary>Most pages scroll inside a centred column; Mail fills the window with its own panes.</summary>
    public bool Scrolls => !FullBleed;

    // Each page is handed to only one of the two hosts, so its view is never built twice.
    public ViewModelBase? ScrollingPage => FullBleed ? null : Page;
    public ViewModelBase? FullPage => FullBleed ? Page : null;
}
