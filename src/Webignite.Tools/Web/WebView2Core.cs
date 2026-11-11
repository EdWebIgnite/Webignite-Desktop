using System.Drawing;
using System.Runtime.Versioning;
using Microsoft.Web.WebView2.Core;
using Webignite.Core.Web;

namespace Webignite.Tools.Web;

/// <summary>
/// One WebView2: the browser Windows already has, drawn inside a window of ours. It runs in its own
/// msedgewebview2.exe processes, which start in <see cref="StartAsync"/> and are gone a moment after
/// <see cref="Dispose"/>. Everything here must be called on the thread that owns the window.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebView2Core : IDisposable
{
    private readonly CoreWebView2Environment environment;
    private readonly CoreWebView2Controller controller;
    private bool closed;

    private WebView2Core(CoreWebView2Environment environment, CoreWebView2Controller controller)
    {
        this.environment = environment;
        this.controller = controller;
        BrowserProcessId = (int)controller.CoreWebView2.BrowserProcessId;
        environment.BrowserProcessExited += OnBrowserExited;
        controller.CoreWebView2.NavigationStarting += OnStarting;
        controller.CoreWebView2.NavigationCompleted += OnCompleted;
        controller.CoreWebView2.NewWindowRequested += OnNewWindow;
    }

    /// <summary>The version of WebView2 on this PC, or null when there is none.</summary>
    public static string? InstalledVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString() is { Length: > 0 } version ? version : null;
        }
#pragma warning disable CA1031 // Whatever goes wrong (no runtime, no loader), the answer is the same: it can't be used.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Starts a browser inside the window with this handle, keeping its cache and cookies in the folder.</summary>
    public static async Task<WebView2Core> StartAsync(IntPtr window, string dataFolder)
    {
        if (InstalledVersion() is null)
            throw new WebEngineException("This PC doesn't have Microsoft Edge WebView2, which this tool draws pages with.") { Missing = true };
        try
        {
            Directory.CreateDirectory(dataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            var controller = await environment.CreateCoreWebView2ControllerAsync(window);
            controller.IsVisible = true;
            return new WebView2Core(environment, controller);
        }
        catch (Exception e) when (e is not WebEngineException)
        {
            throw new WebEngineException("The browser didn't start: " + e.Message, e);
        }
    }

    /// <summary>The main browser process, so a check can see that it has gone after closing.</summary>
    public int BrowserProcessId { get; }

    /// <summary>Completes when the browser's processes have exited after <see cref="Dispose"/>.</summary>
    public Task Exited => exited.Task;

    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<Uri>? Loading;

    public event Action<PageOpened>? Opened;

    /// <summary>Fits the page to its window, in real pixels.</summary>
    public void Resize(int width, int height)
    {
        if (!closed)
            controller.Bounds = new Rectangle(0, 0, Math.Max(1, width), Math.Max(1, height));
    }

    public void Show(bool visible)
    {
        if (!closed)
            controller.IsVisible = visible;
    }

    public void Open(Uri address)
    {
        if (!closed)
            controller.CoreWebView2.Navigate(address.AbsoluteUri);
    }

    public async Task<string> DevToolsAsync(string method, string parametersJson)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        return await controller.CoreWebView2.CallDevToolsProtocolMethodAsync(method, parametersJson);
    }

    public async Task<bool> SavePdfAsync(string path, PdfChoices choices)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        var settings = environment.CreatePrintSettings();
        (settings.PageWidth, settings.PageHeight) = choices.Inches;
        settings.Orientation = choices.Sideways ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
        settings.ShouldPrintBackgrounds = choices.Backgrounds;
        settings.ShouldPrintHeaderAndFooter = false;
        return await controller.CoreWebView2.PrintToPdfAsync(path, settings);
    }

    /// <summary>A PNG of what the browser is showing in its window right now, as the person sees it.</summary>
    public async Task<byte[]> PreviewAsync()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        using var png = new MemoryStream();
        await controller.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
        return png.ToArray();
    }

    private void OnStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var address))
            Loading?.Invoke(address);
    }

    private void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var web = controller.CoreWebView2;
        if (!Uri.TryCreate(web.Source, UriKind.Absolute, out var address))
            return;
        var ok = e.IsSuccess && e.HttpStatusCode < 400;
        var problem = ok ? "" : PageCapture.Problem(e.HttpStatusCode, e.IsSuccess ? "" : e.WebErrorStatus.ToString());
        Opened?.Invoke(new PageOpened(address, ok, e.HttpStatusCode, web.DocumentTitle ?? "", problem));
    }

    // A link that wants a new window opens here instead: there is only this one.
    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var address) && address.Scheme is "http" or "https")
            Open(address);
    }

    private void OnBrowserExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e) => exited.TrySetResult();

    /// <summary>Closes the browser. Its processes exit by themselves a moment later, as nothing else uses them.</summary>
    public void Dispose()
    {
        if (closed)
            return;
        closed = true;
        controller.CoreWebView2.NavigationStarting -= OnStarting;
        controller.CoreWebView2.NavigationCompleted -= OnCompleted;
        controller.CoreWebView2.NewWindowRequested -= OnNewWindow;
        controller.Close();
    }
}
