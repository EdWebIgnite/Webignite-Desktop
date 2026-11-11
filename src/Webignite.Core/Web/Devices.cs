using System.Globalization;
using System.Text.Json;

namespace Webignite.Core.Web;

public enum DeviceKind
{
    Phone,
    Tablet,
    Computer,
}

/// <summary>
/// A phone, tablet or computer a page can be previewed as: its screen in CSS pixels held upright (wide for a
/// computer), how many real pixels make one of those, and what its browser tells sites it is.
/// </summary>
public sealed record Device(string Name, DeviceKind Kind, int Width, int Height, double Density, string UserAgent = "")
{
    /// <summary>Phones and tablets are touched, lay pages out as mobile browsers do, and say who they are.</summary>
    public bool Mobile => Kind != DeviceKind.Computer;

    /// <summary>The screen as held: turned sideways, width and height swap.</summary>
    public (int Width, int Height) Screen(bool sideways) => sideways ? (Height, Width) : (Width, Height);
}

/// <summary>
/// The devices offered in the page preview, and the messages that make the browser behave as one. It is still
/// Edge's engine pretending: sizes, touch and the name sites are told are right, but a fault only Safari on a
/// real iPhone has won't show.
/// </summary>
public static class Devices
{
    private const string IPhoneAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";
    private const string IPadAgent = "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

    private static string Android(string model, bool phone = true) =>
        $"Mozilla/5.0 (Linux; Android 14; {model}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 {(phone ? "Mobile " : "")}Safari/537.36";

    /// <summary>
    /// Every device offered, in the order shown. To add one, add a line. Sizes are the screen in CSS pixels as
    /// the device's own browser reports it, which is what a site's breakpoints see.
    /// </summary>
    public static IReadOnlyList<Device> All { get; } =
    [
        new("iPhone SE", DeviceKind.Phone, 375, 667, 2, IPhoneAgent),
        new("iPhone 14", DeviceKind.Phone, 390, 844, 3, IPhoneAgent),
        new("iPhone 16", DeviceKind.Phone, 393, 852, 3, IPhoneAgent),
        new("iPhone 16 Pro Max", DeviceKind.Phone, 440, 956, 3, IPhoneAgent),
        new("Samsung Galaxy S24", DeviceKind.Phone, 360, 780, 3, Android("SM-S921B")),
        new("Samsung Galaxy S24 Ultra", DeviceKind.Phone, 384, 824, 3.75, Android("SM-S928B")),
        new("Samsung Galaxy A55", DeviceKind.Phone, 360, 780, 3, Android("SM-A556E")),
        new("Budget Android (360 wide)", DeviceKind.Phone, 360, 800, 2, Android("SM-A055F")),
        new("Samsung Galaxy Z Fold, closed", DeviceKind.Phone, 344, 882, 2.625, Android("SM-F946B")),
        new("Samsung Galaxy Z Fold, open", DeviceKind.Tablet, 690, 829, 2.625, Android("SM-F946B")),
        new("Google Pixel 8", DeviceKind.Phone, 412, 915, 2.625, Android("Pixel 8")),
        new("iPad Mini", DeviceKind.Tablet, 744, 1133, 2, IPadAgent),
        new("iPad Air", DeviceKind.Tablet, 820, 1180, 2, IPadAgent),
        new("iPad Pro 13 inch", DeviceKind.Tablet, 1024, 1366, 2, IPadAgent),
        new("Samsung Galaxy Tab S9", DeviceKind.Tablet, 800, 1280, 2, Android("SM-X710", phone: false)),
        new("Laptop", DeviceKind.Computer, 1366, 768, 1),
        new("Desktop", DeviceKind.Computer, 1920, 1080, 1),
    ];

    /// <summary>The smallest and largest screen a typed size may be.</summary>
    public const int Least = 240;

    public const int Most = 3840;

    /// <summary>A device from a typed size, or null when either isn't a sensible number. Up to 1024 wide it is touched like a phone or tablet.</summary>
    public static Device? Custom(string? width, string? height)
    {
        if (!int.TryParse((width ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var w)
            || !int.TryParse((height ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || w < Least || h < Least || w > Most || h > Most)
            return null;
        return new Device("Any size", w <= 480 ? DeviceKind.Phone : w <= 1024 ? DeviceKind.Tablet : DeviceKind.Computer, w, h, 1);
    }

    /// <summary>
    /// How much the screen is shrunk to fit the room there is for it: 1 when it fits, never bigger than life,
    /// and never so small it can't be read.
    /// </summary>
    public static double Fit(int width, int height, double roomWidth, double roomHeight)
    {
        if (roomWidth <= 0 || roomHeight <= 0 || double.IsNaN(roomWidth) || double.IsNaN(roomHeight))
            return 1;
        var fit = Math.Min(1, Math.Min(roomWidth / width, roomHeight / height));
        return Math.Max(0.1, Math.Floor(fit * 100) / 100);
    }

    /// <summary>"390 × 844", with "shown at 80%" when it had to be shrunk.</summary>
    public static string SizeWords(int width, int height, double fit) =>
        $"{width.ToString(CultureInfo.InvariantCulture)} × {height.ToString(CultureInfo.InvariantCulture)}"
        + (fit < 0.995 ? $", shown at {Math.Round(fit * 100).ToString(CultureInfo.InvariantCulture)}%" : "");

    /// <summary>
    /// Makes the browser behave as the device: its screen, pixel density, touch and name. The page should be
    /// loaded again after the device changes, as sites decide what to send from the name.
    /// </summary>
    /// <param name="devTools">Calls one of the browser's developer tools methods with JSON, and gives its JSON answer.</param>
    public static async Task ApplyAsync(Func<string, string, Task<string>> devTools, Device device, bool sideways, double fit)
    {
        var (width, height) = device.Screen(sideways);
        await devTools("Emulation.setDeviceMetricsOverride", ScreenMessage(width, height, device.Density, device.Mobile, fit));
        await devTools("Emulation.setUserAgentOverride", AgentMessage(device.UserAgent));
        await devTools("Emulation.setTouchEmulationEnabled", TouchMessage(device.Mobile));
        await devTools("Emulation.setEmitTouchEventsForMouse", MouseAsTouchMessage(device.Mobile));
    }

    /// <summary>Puts the browser back to being itself, filling its window.</summary>
    public static async Task ClearAsync(Func<string, string, Task<string>> devTools)
    {
        await devTools("Emulation.clearDeviceMetricsOverride", "{}");
        await devTools("Emulation.setUserAgentOverride", AgentMessage(""));
        await devTools("Emulation.setTouchEmulationEnabled", TouchMessage(false));
        await devTools("Emulation.setEmitTouchEventsForMouse", MouseAsTouchMessage(false));
    }

    public static string ScreenMessage(int width, int height, double density, bool mobile, double fit) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["width"] = width, ["height"] = height, ["deviceScaleFactor"] = density, ["mobile"] = mobile, ["scale"] = fit,
        });

    /// <summary>An empty name puts the browser's own back.</summary>
    public static string AgentMessage(string userAgent) => JsonSerializer.Serialize(new Dictionary<string, object> { ["userAgent"] = userAgent });

    public static string TouchMessage(bool on) =>
        JsonSerializer.Serialize(on ? new Dictionary<string, object> { ["enabled"] = true, ["maxTouchPoints"] = 5 } : new Dictionary<string, object> { ["enabled"] = false });

    /// <summary>With this on, dragging with the mouse scrolls the page as a finger would.</summary>
    public static string MouseAsTouchMessage(bool on) =>
        JsonSerializer.Serialize(on ? new Dictionary<string, object> { ["enabled"] = true, ["configuration"] = "mobile" } : new Dictionary<string, object> { ["enabled"] = false });
}
