using System.Runtime.Versioning;
using Webignite.Tools.Web;

namespace Webignite.Desktop.Web;

/// <summary>
/// A browser nobody sees, for reading pages that scripts build: the Site crawler's, when asked to. Like
/// <see cref="IWebEngine"/> it lives only as long as the job that asked for it, and disposing it shuts the
/// browser down.
/// </summary>
public interface IPageReader : IDisposable
{
    /// <summary>Starts the browser. Throws <see cref="WebEngineException"/> in plain words when it can't.</summary>
    Task StartAsync();

    /// <summary>A page's HTML after its scripts ran, or null when the browser got nothing from it.</summary>
    Task<string?> ReadAsync(Uri address, CancellationToken cancel);
}

/// <summary>The page reader on Windows: a WebView2 with no window, used from the screen's thread.</summary>
[SupportedOSPlatform("windows")]
public sealed class WebView2Reader(string dataFolder) : IPageReader
{
    private WebView2Core? core;
    private bool disposed;

    public async Task StartAsync()
    {
        if (core is not null || disposed)
            return;
        var started = await WebView2Core.StartAsync(WebView2Core.NoWindow, dataFolder);
        if (disposed)
        {
            started.Dispose();
            return;
        }
        core = started;
    }

    // The crawler may come back on any thread; WebView2 must be used from the one it was started on.
    public Task<string?> ReadAsync(Uri address, CancellationToken cancel) =>
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => core?.ReadRenderedAsync(address, cancel) ?? Task.FromResult<string?>(null));

    public void Dispose()
    {
        disposed = true;
        core?.Dispose();
        core = null;
    }
}
