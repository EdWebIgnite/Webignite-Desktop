using Avalonia.Controls;
using Webignite.Core.Web;
using Webignite.Tools.Web;

namespace Webignite.Desktop.Web;

/// <summary>
/// A browser a web tool draws pages with, alive only while that tool's window is open. On Windows it is the
/// WebView2 the PC already has (<see cref="WebView2Engine"/>); the screenshot tool, which runs on Linux, hands
/// in a stand-in. Disposing it must shut the browser down and leave no process behind. It is declared here, not
/// in Webignite.Tools, so AppServices can offer one without that library being read at start-up.
/// </summary>
public interface IWebEngine : IDisposable
{
    /// <summary>What the tool's window shows: the page itself.</summary>
    Control View { get; }

    /// <summary>Starts the browser once <see cref="View"/> is in a window. Throws <see cref="WebEngineException"/> in plain words when it can't.</summary>
    Task StartAsync();

    /// <summary>Starts opening a page. <see cref="Opened"/> says how it went.</summary>
    void Open(Uri address);

    /// <summary>A page started loading, from <see cref="Open"/> or a link clicked in the page.</summary>
    event Action<Uri>? Loading;

    /// <summary>A page finished loading, or failed to.</summary>
    event Action<PageOpened>? Opened;

    /// <summary>Calls one of the browser's developer tools methods with its parameters as JSON, and gives its JSON answer.</summary>
    Task<string> DevToolsAsync(string method, string parametersJson);

    /// <summary>Saves the open page as a PDF. False when the browser couldn't.</summary>
    Task<bool> SavePdfAsync(string path, PdfChoices choices);
}
