using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Webignite.Core.Web;
using Webignite.Tools;
using Webignite.Tools.Web;

namespace Webignite.Tests;

/// <summary>
/// The real WebView2, on Windows only (GitHub's Windows build has it, as most PCs do). It is the one part of the
/// web tools that can't be tried anywhere else: that the browser starts inside a window, opens a page, gives a
/// picture and a PDF of it, and that closing it leaves no browser process behind.
/// </summary>
public class WebView2Tests
{
    [Fact]
    public void Starts_pictures_a_page_saves_a_pdf_and_leaves_nothing_running()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Assert.SkipWhen(WebView2Core.InstalledVersion() is null, "This PC has no WebView2.");

        var folder = Path.Combine(Path.GetTempPath(), "webignite-webview2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var page = Path.Combine(folder, "tall.html");
        File.WriteAllText(page, """
            <html><head><title>Tall page</title><style>body{margin:0}</style></head>
            <body><h1>Hello</h1><div style="height:3000px;background:linear-gradient(#fff,#e8590c)"></div><p>End</p></body></html>
            """);
        var pdf = Path.Combine(folder, "tall.pdf");
        try
        {
            var (opened, shot, pdfSaved, browserGone) = Use(folder, page, pdf);

            Assert.True(opened.Ok, opened.Problem);
            Assert.Equal("Tall page", opened.Title);
            Assert.Equal(390, shot.Width);
            Assert.InRange(shot.Height, 3000, 3400);
            Assert.False(shot.Cut);
            Assert.Equal((shot.Width, shot.Height), PngSize(shot.Png));
            Assert.True(pdfSaved);
            Assert.True(PdfText.Pages(File.ReadAllBytes(pdf)) >= 3, "A page 3 000 pixels tall takes a few sheets of A4");
            Assert.True(browserGone, "The browser's processes should exit once the tool is closed");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The browser may still be letting go of its cache; the temp folder is cleared by Windows in time.
            }
        }
    }

    /// <summary>
    /// The phone view: the browser is told to be a screen twice the size of its window, shrunk to half. The page
    /// must believe the screen's size and name, and what is drawn must be the whole screen shrunk, not its corner.
    /// </summary>
    [Fact]
    public void Shown_as_a_device_the_page_believes_it_and_is_drawn_shrunk_to_fit()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Assert.SkipWhen(WebView2Core.InstalledVersion() is null, "This PC has no WebView2.");

        var folder = Path.Combine(Path.GetTempPath(), "webignite-webview2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var page = Path.Combine(folder, "halves.html");
        File.WriteAllText(page, """
            <html><head><title>Halves</title><meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="margin:0">
            <div style="position:fixed;left:0;top:0;width:50%;height:100%;background:#ff0000"></div>
            <div style="position:fixed;left:50%;top:0;width:50%;height:100%;background:#0000ff"></div>
            </body></html>
            """);
        try
        {
            var seen = ShowAsDevice(folder, page);

            Assert.Equal("800|600", seen.AsDevice[0] + "|" + seen.AsDevice[1]);
            Assert.Equal("2", seen.AsDevice[2]);
            Assert.Contains("WebigniteTestPhone", seen.AsDevice[3], StringComparison.Ordinal);
            Assert.True(int.Parse(seen.AsDevice[4], System.Globalization.CultureInfo.InvariantCulture) > 0, "A phone is touched");

            using var drawn = SkiaSharp.SKBitmap.Decode(seen.Preview);
            var left = drawn.GetPixel(drawn.Width / 4, drawn.Height / 2);
            var right = drawn.GetPixel(drawn.Width * 3 / 4, drawn.Height / 2);
            Assert.True(left.Red > 200 && left.Blue < 60, $"The left of what is drawn should be the page's red half, but is {left} in a {drawn.Width} x {drawn.Height} picture");
            Assert.True(right.Blue > 200 && right.Red < 60, $"The right of what is drawn should be the page's blue half, but is {right} in a {drawn.Width} x {drawn.Height} picture");

            Assert.NotEqual("800", seen.Back[0]);
            Assert.DoesNotContain("WebigniteTestPhone", seen.Back[3], StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // As above.
            }
        }
    }

    /// <summary>
    /// The crawler's browser: one with no window at all. It must still run a page's scripts, so links a script
    /// adds a moment after loading are in the HTML it gives back.
    /// </summary>
    [Fact]
    public void With_no_window_a_page_built_by_scripts_is_read_once_it_is_built()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Assert.SkipWhen(WebView2Core.InstalledVersion() is null, "This PC has no WebView2.");

        var folder = Path.Combine(Path.GetTempPath(), "webignite-webview2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var page = Path.Combine(folder, "built.html");
        File.WriteAllText(page, """
            <html><head><title>Loading</title></head><body><div id="root"></div>
            <script>
              setTimeout(() => {
                document.title = "Built by script";
                document.getElementById("root").innerHTML = "<h1>Menu</h1><a href='/tea'>Tea</a>";
                setTimeout(() => { document.getElementById("root").insertAdjacentHTML("beforeend", "<a href='/coffee'>Coffee</a>"); }, 300);
              }, 400);
            </script></body></html>
            """);
        try
        {
            var (html, missing) = ReadBuilt(folder, page);

            Assert.NotNull(html);
            var facts = HtmlFacts.Read(html);
            Assert.Equal("Built by script", facts.Title);
            Assert.Equal(["/tea", "/coffee"], facts.Links.Select(l => l.Href));
            Assert.Null(missing);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // As above.
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static (string? Html, string? Missing) ReadBuilt(string folder, string page) => OnAWindowThread(async _ =>
    {
        using var core = await WebView2Core.StartAsync(WebView2Core.NoWindow, Path.Combine(folder, "browser"));
        var html = await core.ReadRenderedAsync(new Uri(page), CancellationToken.None);
        // A page that isn't there gives nothing, and the reader carries on.
        var missing = await core.ReadRenderedAsync(new Uri(Path.Combine(folder, "nothere.html")), CancellationToken.None);
        return (html, missing);
    });

    private sealed record DeviceSeen(string[] AsDevice, byte[] Preview, string[] Back);

    [SupportedOSPlatform("windows")]
    private static DeviceSeen ShowAsDevice(string folder, string page) => OnAWindowThread(window => ShowAsDeviceAsync(window, folder, page));

    [SupportedOSPlatform("windows")]
    private static async Task<DeviceSeen> ShowAsDeviceAsync(IntPtr window, string folder, string page)
    {
        using var core = await WebView2Core.StartAsync(window, Path.Combine(folder, "browser"));
        core.Resize(400, 300);
        var opened = new TaskCompletionSource<PageOpened>();
        core.Opened += result => opened.TrySetResult(result);
        core.Open(new Uri(page));
        await opened.Task;

        var phone = new Device("Test phone", DeviceKind.Phone, 800, 600, 2, "Mozilla/5.0 (WebigniteTestPhone)");
        await Devices.ApplyAsync(core.DevToolsAsync, phone, sideways: false, fit: 0.5);
        var again = new TaskCompletionSource<PageOpened>();
        core.Opened += result => again.TrySetResult(result);
        await core.DevToolsAsync("Page.reload", "{}");
        await again.Task;
        await Task.Delay(700);
        var asDevice = await TellAsync(core);
        var preview = await core.PreviewAsync();

        await Devices.ClearAsync(core.DevToolsAsync);
        await Task.Delay(300);
        return new DeviceSeen(asDevice, preview, await TellAsync(core));
    }

    // What the page itself sees: its width and height, pixel density, the name it gives sites, and touch points.
    [SupportedOSPlatform("windows")]
    private static async Task<string[]> TellAsync(WebView2Core core)
    {
        var answer = await core.DevToolsAsync("Runtime.evaluate",
            """{"expression":"[innerWidth, innerHeight, devicePixelRatio, navigator.userAgent, navigator.maxTouchPoints].join('|')","returnByValue":true}""");
        using var json = System.Text.Json.JsonDocument.Parse(answer);
        return json.RootElement.GetProperty("result").GetProperty("value").GetString()!.Split('|');
    }

    [SupportedOSPlatform("windows")]
    private static (PageOpened Opened, PageShot Shot, bool PdfSaved, bool BrowserGone) Use(string folder, string page, string pdf) =>
        OnAWindowThread(window => UseAsync(window, folder, page, pdf));

    [SupportedOSPlatform("windows")]
    private static async Task<(PageOpened Opened, PageShot Shot, bool PdfSaved, bool BrowserGone)> UseAsync(IntPtr window, string folder, string page, string pdf)
    {
        var core = await WebView2Core.StartAsync(window, Path.Combine(folder, "browser"));
        var browser = core.BrowserProcessId;
        PageOpened opened;
        PageShot shot;
        bool pdfSaved;
        try
        {
            core.Resize(1000, 700);
            var done = new TaskCompletionSource<PageOpened>();
            core.Opened += result => done.TrySetResult(result);
            core.Open(new Uri(page));
            opened = await done.Task;
            shot = await PageCapture.ShootAsync(core.DevToolsAsync, PageCapture.Widths[0], wholePage: true);
            pdfSaved = await core.SavePdfAsync(pdf, new PdfChoices());
        }
        finally
        {
            core.Dispose();
        }
        // The browser exits by itself once its only page is closed. Give it a while; it is usually a second or two.
        await Task.WhenAny(core.Exited, Task.Delay(TimeSpan.FromSeconds(30)));
        return (opened, shot, pdfSaved, Gone(browser));
    }

    private static bool Gone(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static (int, int) PngSize(byte[] png) =>
        ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);

    // WebView2 needs what a desktop app has: a window, and a thread that owns it and keeps handing out its
    // messages. This is the least of that, so the test needs no app around it.
    [SupportedOSPlatform("windows")]
    private static T OnAWindowThread<T>(Func<IntPtr, Task<T>> work)
    {
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var posted = new Posted();
            SynchronizationContext.SetSynchronizationContext(posted);
            // WS_OVERLAPPEDWINDOW | WS_VISIBLE: an ordinary window on screen, as the tool's is.
            var window = CreateWindowExW(0, "STATIC", "Webignite test", 0x00CF0000 | 0x10000000, 0, 0, 1000, 700, 0, 0, 0, 0);
            try
            {
                var task = work(window);
                var clock = Stopwatch.StartNew();
                while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromMinutes(3))
                {
                    while (PeekMessageW(out var message, 0, 0, 0, 1))
                    {
                        TranslateMessage(in message);
                        DispatchMessageW(in message);
                    }
                    posted.Run();
                    Thread.Sleep(5);
                }
                if (!task.IsCompleted)
                    failure = new TimeoutException("WebView2 didn't finish in 3 minutes.");
                else if (task.IsFaulted)
                    failure = task.Exception!.GetBaseException();
                else
                    result = task.Result;
            }
            finally
            {
                DestroyWindow(window);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("WebView2 on a window thread: " + failure.Message, failure);
        return result!;
    }

    /// <summary>Brings what follows an await back to the window's thread, as an app's own dispatcher does.</summary>
    private sealed class Posted : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> waiting = new();

        public override void Post(SendOrPostCallback d, object? state) => waiting.Enqueue((d, state));

        public void Run()
        {
            while (waiting.TryDequeue(out var next))
                next.Callback(next.State);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Kind;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Extra;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out Message message, IntPtr window, uint first, uint last, uint remove);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in Message message);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr DispatchMessageW(in Message message);
}
