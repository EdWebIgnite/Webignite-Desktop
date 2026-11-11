using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;
using Webignite.Desktop.Files;
using Webignite.Desktop.Web;

namespace Webignite.Desktop.ViewModels;

// Page to PDF or picture: a web page drawn by the browser Windows already has, saved as a PDF or pictured at
// phone, tablet and desktop widths. The browser starts when this window opens and is shut down when it closes.
public sealed partial class ToolsViewModel
{
    private IWebEngine? browser;
    private Uri? capturePage;

    [ObservableProperty] public partial string CaptureAddress { get; set; } = "";
    [ObservableProperty] public partial string CaptureNote { get; set; } = "";
    [ObservableProperty] public partial bool CaptureProblem { get; set; }

    /// <summary>The page itself, drawn by the browser. Null until it starts, and when there is no browser.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaptureView))]
    public partial Control? CaptureView { get; set; }

    /// <summary>Said in place of the page when there is no browser to draw it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaptureView))]
    public partial string CaptureWithout { get; set; } = "";

    [ObservableProperty] public partial bool CaptureNeedsWebView2 { get; set; }

    /// <summary>A page is open and nothing is being saved, so it can be saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePagePdfCommand), nameof(SavePagePicturesCommand), nameof(MarkUpPageCommand))]
    public partial bool CaptureReady { get; set; }

    [ObservableProperty] public partial bool CaptureWorking { get; set; }

    public bool HasCaptureView => CaptureView is not null && CaptureWithout.Length == 0;

    public IReadOnlyList<PaperChoice> Papers { get; } =
        [new(PaperSize.A4, "A4"), new(PaperSize.A3, "A3"), new(PaperSize.Letter, "Letter (US)")];

    [ObservableProperty] public partial PaperChoice? Paper { get; set; }
    [ObservableProperty] public partial bool PdfSideways { get; set; }
    [ObservableProperty] public partial bool PdfBackgrounds { get; set; } = true;

    /// <summary>The screens a picture can be taken at. Phone and Desktop are ticked to begin with.</summary>
    public IReadOnlyList<ScreenChoice> CaptureScreens { get; } =
        [.. PageCapture.Widths.Select(w => new ScreenChoice(w) { IsOn = w.Name is "Phone" or "Desktop" })];

    [ObservableProperty] public partial bool CaptureWholePage { get; set; } = true;

    // The preview: the page filling the window, or shown as a phone, tablet or computer screen.

    /// <summary>Fit the window first, then each device, then a size typed in.</summary>
    public IReadOnlyList<DeviceChoice> CaptureDevices { get; } =
        [new DeviceChoice("Fit the window", null), .. Devices.All.Select(d => new DeviceChoice(d.Name, d)), new DeviceChoice("Any size", null, Custom: true)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDevice), nameof(IsDeviceOn))]
    public partial DeviceChoice? CaptureDevice { get; set; }

    [ObservableProperty] public partial bool CaptureSideways { get; set; }
    [ObservableProperty] public partial string CustomWidth { get; set; } = "600";
    [ObservableProperty] public partial string CustomHeight { get; set; } = "900";

    /// <summary>"390 × 844, shown at 80%", or why the typed size can't be used.</summary>
    [ObservableProperty] public partial string DeviceSize { get; set; } = "";

    /// <summary>The size the page is drawn at on screen. Not a number when it fills the window.</summary>
    [ObservableProperty] public partial double PaneWidth { get; set; } = double.NaN;
    [ObservableProperty] public partial double PaneHeight { get; set; } = double.NaN;

    public bool IsCustomDevice => CaptureDevice?.Custom == true;
    public bool IsDeviceOn => ActiveDevice is not null;

    private Device? ActiveDevice => CaptureDevice is null ? null : CaptureDevice.Custom ? Devices.Custom(CustomWidth, CustomHeight) : CaptureDevice.Device;

    private double roomWidth;
    private double roomHeight;
    private Task deviceWork = Task.CompletedTask;

    partial void OnCaptureDeviceChanged(DeviceChoice? value) => ShowAsDevice(reload: true);

    partial void OnCaptureSidewaysChanged(bool value) => ShowAsDevice(reload: false);

    partial void OnCustomWidthChanged(string value) => ShowAsDevice(reload: false);

    partial void OnCustomHeightChanged(string value) => ShowAsDevice(reload: false);

    /// <summary>How much room the window has for the page. Called by the view as the window is resized.</summary>
    public void SetCaptureRoom(double width, double height)
    {
        if (Math.Abs(width - roomWidth) < 0.5 && Math.Abs(height - roomHeight) < 0.5)
            return;
        (roomWidth, roomHeight) = (width, height);
        ShowAsDevice(reload: false);
    }

    // Sizes the page's pane to the device and tells the browser to behave as it. Changes queue up, and each
    // reads the choices as they are when its turn comes, so the last one always wins.
    private void ShowAsDevice(bool reload)
    {
        var device = ActiveDevice;
        OnPropertyChanged(nameof(IsDeviceOn));
        var fit = 1.0;
        if (device is null)
        {
            (PaneWidth, PaneHeight) = (double.NaN, double.NaN);
            DeviceSize = IsCustomDevice ? $"Type a width and height between {Devices.Least} and {Devices.Most}." : "";
        }
        else
        {
            var (width, height) = device.Screen(CaptureSideways);
            fit = Devices.Fit(width, height, roomWidth, roomHeight);
            (PaneWidth, PaneHeight) = (Math.Round(width * fit), Math.Round(height * fit));
            DeviceSize = Devices.SizeWords(width, height, fit);
        }
        if (browser is null || !browserStarted || CaptureWorking)
            return;
        // The browser is only told when what it should be has changed: resizing the window with no device
        // chosen, say, changes nothing for it.
        var wanted = (device, device is not null && CaptureSideways, fit);
        if (!reload && wanted == shownAs)
            return;
        shownAs = wanted;
        deviceWork = ApplyDeviceAsync(deviceWork, reload);
        deviceWork.Forget("Showing the page as a device");
    }

    private async Task ApplyDeviceAsync(Task before, bool reload)
    {
        try
        {
            await before;
        }
#pragma warning disable CA1031 // An earlier change that failed was logged by its own Forget; this one still gets its turn.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        if (browser is not { } open || CaptureWorking)
            return;
        if (ActiveDevice is { } device)
        {
            var (width, height) = device.Screen(CaptureSideways);
            await Devices.ApplyAsync(open.DevToolsAsync, device, CaptureSideways, Devices.Fit(width, height, roomWidth, roomHeight));
        }
        else
        {
            await Devices.ClearAsync(open.DevToolsAsync);
        }
        // Sites choose what to send by the device's name, so the page is fetched again as the new one.
        if (reload && capturePage is not null)
            await open.DevToolsAsync("Page.reload", "{}");
    }

    private bool browserStarted;

    // What the browser was last told to be: no device, filling its window, to begin with.
    private (Device? Device, bool Sideways, double Fit)? shownAs = (null, false, 1.0);

    /// <summary>Starts the browser. Called by the window once it is open, never before.</summary>
    public async Task StartCaptureAsync()
    {
        if (browser is not null)
            return;
        Paper ??= Papers[0];
        CaptureDevice ??= CaptureDevices[0];
        var made = services.NewBrowser();
        if (made is null)
        {
            CaptureWithout = "This tool draws pages with Microsoft Edge WebView2, which is part of Windows.";
            return;
        }
        browser = made;
        made.Loading += OnCaptureLoading;
        made.Opened += OnCaptureOpened;
        CaptureView = made.View;
        CaptureNote = "Starting the browser…";
        try
        {
            await made.StartAsync();
            if (browser != made)
                return;
            browserStarted = true;
            if (IsDeviceOn)
            {
                shownAs = null;
                ShowAsDevice(reload: false);
            }
            CaptureNote = "Type a web address and press Enter. Once the page is open, close any cookie banner in it before saving.";
            if (capturePage is null && PageCapture.Address(CaptureAddress) is not null)
                OpenCapturePage();
        }
        catch (Webignite.Tools.Web.WebEngineException e)
        {
            StopCapture();
            CaptureNeedsWebView2 = e.Missing;
            CaptureWithout = e.Missing
                ? e.Message + " It comes with Windows 11 and with Microsoft Edge; on a PC without it, it is a free download from Microsoft."
                : e.Message;
            CaptureNote = "";
        }
        catch (OperationCanceledException)
        {
            // The window was closed while the browser was starting.
        }
    }

    /// <summary>Shuts the browser down. Called when the window closes.</summary>
    public void StopCapture()
    {
        if (browser is not { } open)
            return;
        browser = null;
        browserStarted = false;
        shownAs = (null, false, 1.0);
        open.Loading -= OnCaptureLoading;
        open.Opened -= OnCaptureOpened;
        CaptureView = null;
        CaptureReady = false;
        capturePage = null;
        open.Dispose();
    }

    [RelayCommand]
    private void OpenCapturePage()
    {
        if (browser is null || CaptureWorking)
            return;
        if (PageCapture.Address(CaptureAddress) is not { } address)
        {
            CaptureNote = "That isn't a web address. Try something like karoocoffee.co.za.";
            CaptureProblem = true;
            return;
        }
        browser.Open(address);
    }

    private void OnCaptureLoading(Uri address)
    {
        if (address.Scheme is not ("http" or "https"))
            return;
        CaptureReady = false;
        CaptureProblem = false;
        CaptureAddress = address.AbsoluteUri;
        CaptureNote = "Opening…";
    }

    private void OnCaptureOpened(PageOpened page)
    {
        capturePage = page.Ok ? page.Address : null;
        CaptureReady = page.Ok && !CaptureWorking;
        CaptureProblem = !page.Ok;
        CaptureNote = page.Ok ? (page.Title.Length > 0 ? page.Title : page.Address.Host) : page.Problem;
    }

    private bool CanCapture() => CaptureReady;

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task SavePagePdf(Visual from)
    {
        if (browser is null || capturePage is not { } page)
            return;
        var path = await Picker.Save(from, "Save the page as a PDF", PageCapture.PdfName(page), ["*.pdf"], "PDF");
        if (path is not null)
            await SavePagePdfToAsync(path);
    }

    /// <summary>Saves the open page as a PDF at this path.</summary>
    public async Task SavePagePdfToAsync(string path)
    {
        if (browser is not { } open || capturePage is null)
            return;
        await CaptureAsync("Making the PDF…", async () =>
        {
            if (!await open.SavePdfAsync(path, new PdfChoices(Paper?.Size ?? PaperSize.A4, PdfSideways, PdfBackgrounds)))
                throw new Webignite.Tools.Web.WebEngineException("The browser couldn't make a PDF of that page.");
            capturedFile = path;
            return "Saved as " + Path.GetFileName(path) + ".";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task SavePagePictures(Visual from)
    {
        if (browser is null || capturePage is null)
            return;
        if (!IsDeviceOn && !CaptureScreens.Any(s => s.IsOn))
        {
            CaptureNote = "Tick at least one screen to take a picture at.";
            CaptureProblem = true;
            return;
        }
        var folder = await Picker.Folder(from, "Where to save the pictures");
        if (folder is not null)
            await SavePagePicturesToAsync(folder);
    }

    /// <summary>Takes a picture at each ticked screen and saves them in this folder, named after the page.</summary>
    public async Task SavePagePicturesToAsync(string folder)
    {
        if (browser is not { } open || capturePage is not { } page)
            return;
        var (screens, mobile) = ScreensToShoot();
        var whole = CaptureWholePage;
        await CaptureAsync("Taking the pictures…", async () =>
        {
            var cut = 0;
            foreach (var screen in screens)
            {
                CaptureNote = $"Taking the {screen.Name} picture…";
                var shot = await PageCapture.ShootAsync(open.DevToolsAsync, screen, whole, mobile);
                var name = PageCapture.Free(PageCapture.ShotName(page, screen), n => File.Exists(Path.Combine(folder, n)));
                capturedFile = Path.Combine(folder, name);
                await File.WriteAllBytesAsync(capturedFile, shot.Png);
                cut += shot.Cut ? 1 : 0;
            }
            return PageCapture.Saved(screens.Count, cut);
        });
    }

    /// <summary>Takes one picture, at the first ticked screen, and opens it in Mark-up to draw on.</summary>
    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task MarkUpPage()
    {
        if (browser is not { } open || capturePage is not { } page)
            return;
        var (screens, mobile) = ScreensToShoot();
        var screen = screens.Count > 0 ? screens[0] : CaptureScreens[^1].Screen;
        var whole = CaptureWholePage;
        string? picture = null;
        await CaptureAsync("Taking the picture…", async () =>
        {
            var shot = await PageCapture.ShootAsync(open.DevToolsAsync, screen, whole, mobile);
            var folder = Path.Combine(Path.GetTempPath(), "Webignite", "pages");
            Directory.CreateDirectory(folder);
            picture = Path.Combine(folder, PageCapture.ShotName(page, screen));
            await File.WriteAllBytesAsync(picture, shot.Png);
            return "Opened in Mark-up." + (shot.Cut ? $" The page is very long, so it stops at {PageCapture.Tallest} pixels down." : "");
        });
        if (picture is not null)
            services.Go.Markup(picture);
    }

    // As a device, the picture is of that device's screen; otherwise one for each ticked screen.
    private (IReadOnlyList<ShotWidth> Screens, bool Mobile) ScreensToShoot()
    {
        if (ActiveDevice is not { } device)
            return ([.. CaptureScreens.Where(s => s.IsOn).Select(s => s.Screen)], false);
        var (width, height) = device.Screen(CaptureSideways);
        return ([new ShotWidth(device.Name, width, height)], device.Mobile);
    }

    private string? capturedFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCapturedCommand))]
    public partial bool HasCaptured { get; set; }

    [RelayCommand(CanExecute = nameof(HasCaptured))]
    private void ShowCaptured()
    {
        if (capturedFile is { } file && File.Exists(file))
            services.Go.ShowFile(file);
    }

    [RelayCommand]
    private static Task GetWebView2() => AppServices.OpenInBrowser(new Uri("https://developer.microsoft.com/microsoft-edge/webview2/"));

    // One job at a time: the browser is laid out at another width while a picture is taken.
    private async Task CaptureAsync(string doing, Func<Task<string>> work)
    {
        CaptureWorking = true;
        CaptureReady = false;
        CaptureProblem = false;
        CaptureNote = doing;
        capturedFile = null;
        HasCaptured = false;
        try
        {
            CaptureNote = await work();
            HasCaptured = capturedFile is not null;
        }
#pragma warning disable CA1031 // Whatever stopped it (the disk, the browser, the page), the person is told here and carries on.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Webignite.Data.AppLog.Write("Saving a web page didn't work", e);
            CaptureNote = "That didn't work: " + e.Message;
            CaptureProblem = true;
        }
        finally
        {
            CaptureWorking = false;
            CaptureReady = browser is not null && capturePage is not null;
            // Taking a picture lays the browser out afresh; put the device being shown back.
            if (IsDeviceOn)
            {
                shownAs = null;
                ShowAsDevice(reload: false);
            }
        }
    }
}

public sealed record DeviceChoice(string Label, Device? Device, bool Custom = false)
{
    public override string ToString() => Label;
}

public sealed record PaperChoice(PaperSize Size, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class ScreenChoice(ShotWidth screen) : ObservableObject
{
    public ShotWidth Screen { get; } = screen;
    public string Label => Screen.Label;

    [ObservableProperty] public partial bool IsOn { get; set; }
}
