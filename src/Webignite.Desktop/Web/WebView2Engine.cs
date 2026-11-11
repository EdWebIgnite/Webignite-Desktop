using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Webignite.Core.Web;
using Webignite.Tools.Web;

namespace Webignite.Desktop.Web;

/// <summary>
/// The browser for the web tools on Windows: a <see cref="WebView2Core"/> drawn in a pane of the tool's window.
/// Nothing of WebView2 is touched until <see cref="StartAsync"/>, and closing the window (or disposing this)
/// shuts it down.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebView2Engine : IWebEngine
{
    private readonly Pane pane;
    private readonly string dataFolder;
    private WebView2Core? core;
    private bool disposed;

    /// <param name="dataFolder">Where the browser keeps its cache and cookies, apart from Edge's own.</param>
    public WebView2Engine(string dataFolder)
    {
        this.dataFolder = dataFolder;
        pane = new Pane(this);
    }

    public Control View => pane;

    public event Action<Uri>? Loading;

    public event Action<PageOpened>? Opened;

    public async Task StartAsync()
    {
        if (core is not null || disposed)
            return;
        var window = await pane.Window.Task;
        var started = await WebView2Core.StartAsync(window, dataFolder);
        if (disposed || core is not null)
        {
            // The window was closed while the browser was starting.
            started.Dispose();
            return;
        }
        core = started;
        started.Loading += address => Loading?.Invoke(address);
        started.Opened += page => Opened?.Invoke(page);
        pane.Fit();
    }

    public void Open(Uri address) => core?.Open(address);

    public Task<string> DevToolsAsync(string method, string parametersJson) =>
        core?.DevToolsAsync(method, parametersJson) ?? throw new WebEngineException("The browser isn't running.");

    public Task<bool> SavePdfAsync(string path, PdfChoices choices) =>
        core?.SavePdfAsync(path, choices) ?? throw new WebEngineException("The browser isn't running.");

    public void Dispose()
    {
        disposed = true;
        core?.Dispose();
        core = null;
        pane.Window.TrySetCanceled();
    }

    /// <summary>The pane of the window the page is drawn in: a plain child window that WebView2 fills.</summary>
    private sealed class Pane(WebView2Engine owner) : NativeControlHost
    {
        private TopLevel? top;

        /// <summary>The pane's own window handle, once it is on screen.</summary>
        public TaskCompletionSource<IntPtr> Window { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            var handle = base.CreateNativeControlCore(parent);
            Window.TrySetResult(handle.Handle);
            return handle;
        }

        // The browser goes before the window it is drawn in does.
        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            owner.Dispose();
            base.DestroyNativeControlCore(control);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            top = TopLevel.GetTopLevel(this);
            if (top is not null)
                top.ScalingChanged += OnScalingChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (top is not null)
                top.ScalingChanged -= OnScalingChanged;
            top = null;
            base.OnDetachedFromVisualTree(e);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = base.ArrangeOverride(finalSize);
            Fit(size);
            return size;
        }

        private void OnScalingChanged(object? sender, EventArgs e) => Fit();

        public void Fit() => Fit(Bounds.Size);

        // WebView2 is sized in real pixels; Avalonia measures in scaled ones.
        private void Fit(Size size)
        {
            var scale = top?.RenderScaling ?? 1;
            owner.core?.Resize((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        }
    }
}
