using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Webignite.Core.Web;

/// <summary>A screen a page is pictured at: how wide and how tall the browser's window is made.</summary>
public sealed record ShotWidth(string Name, int Width, int Height)
{
    public string Label => $"{Name} ({Width} px)";
}

public enum PaperSize
{
    A4,
    A3,
    Letter,
}

/// <summary>How a page is laid out when saved as a PDF.</summary>
public sealed record PdfChoices(PaperSize Paper = PaperSize.A4, bool Sideways = false, bool Backgrounds = true)
{
    /// <summary>The sheet held upright, in inches, which is what the browser asks for.</summary>
    public (double Width, double Height) Inches => Paper switch
    {
        PaperSize.A3 => (11.69, 16.54),
        PaperSize.Letter => (8.5, 11),
        _ => (8.27, 11.69),
    };
}

/// <summary>A page as the browser left it after opening.</summary>
public sealed record PageOpened(Uri Address, bool Ok, int Status, string Title, string Problem = "");

/// <summary>A picture of a page: the PNG, its size, and whether a very long page had to be cut off.</summary>
public sealed record PageShot(byte[] Png, int Width, int Height, bool Cut);

/// <summary>
/// The rules for saving a web page as a PDF or as pictures: the screens offered, how tall a picture may be,
/// what the files are called, and the steps and messages that take a picture through a browser's developer tools.
/// The browser itself is in Webignite.Tools; nothing here needs one.
/// </summary>
public static class PageCapture
{
    /// <summary>The screens offered. The phone is the standard iPhone of the last few years; the laptop the most common one.</summary>
    public static IReadOnlyList<ShotWidth> Widths { get; } =
    [
        new("Phone", 390, 844),
        new("Tablet", 768, 1024),
        new("Laptop", 1366, 768),
        new("Desktop", 1920, 1080),
    ];

    /// <summary>The tallest picture taken. Browsers can't draw one much taller, so a longer page is cut off here.</summary>
    public const int Tallest = 16000;

    /// <summary>What was typed as a web address, with https:// added when left off. Null when it isn't one.</summary>
    public static Uri? Address(string? typed) => SiteCrawl.Start(typed);

    /// <summary>How tall the picture is: the screen's height, or the whole page down to the tallest allowed.</summary>
    public static (int Height, bool Cut) Height(ShotWidth screen, double pageHeight, bool wholePage)
    {
        if (!wholePage || double.IsNaN(pageHeight) || pageHeight <= screen.Height)
            return (screen.Height, false);
        var whole = (int)Math.Ceiling(pageHeight);
        return whole > Tallest ? (Tallest, true) : (whole, false);
    }

    /// <summary>A file name from the address: "karoocoffee-co-za-about". Safe on every disk, and short.</summary>
    public static string Name(Uri address)
    {
        var host = address.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? address.Host[4..] : address.Host;
        var name = Slug(host + Uri.UnescapeDataString(address.AbsolutePath));
        if (name.Length > 80)
            name = name[..80].TrimEnd('-');
        return name.Length == 0 ? "page" : name;
    }

    // Letters and digits in lower case, everything else a single dash.
    private static string Slug(string words)
    {
        var text = new StringBuilder();
        foreach (var c in words)
        {
            if (char.IsAsciiLetterOrDigit(c))
                text.Append(char.ToLowerInvariant(c));
            else if (text.Length > 0 && text[^1] != '-')
                text.Append('-');
        }
        return text.ToString().Trim('-');
    }

    public static string PdfName(Uri address) => Name(address) + ".pdf";

    /// <summary>"karoocoffee-co-za-menu-phone-390.png": the page, the screen's name and its width.</summary>
    public static string ShotName(Uri address, ShotWidth screen) =>
        $"{Name(address)}-{Slug(screen.Name)}-{screen.Width.ToString(CultureInfo.InvariantCulture)}.png";

    /// <summary>A name not yet used in the folder: "name.png", then "name (2).png".</summary>
    public static string Free(string name, Func<string, bool> taken)
    {
        if (!taken(name))
            return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ending = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var next = $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}){ending}";
            if (!taken(next))
                return next;
        }
    }

    /// <summary>What to say when pictures were saved.</summary>
    public static string Saved(int pictures, int cut) =>
        (pictures == 1 ? "1 picture saved." : $"{pictures} pictures saved.")
        + (cut == 0 ? "" : $" The page is very long, so {(pictures == 1 ? "it stops" : cut == pictures ? "they stop" : "some stop")} at {Tallest} pixels down.");

    /// <summary>What to say when the page didn't open, from the browser's own word for what went wrong.</summary>
    public static string Problem(int status, string browserSaid)
    {
        if (status >= 400)
            return $"The site answered {status.ToString(CultureInfo.InvariantCulture)} ({SiteCrawl.StatusWords(status)}).";
        return browserSaid switch
        {
            "HostNameNotResolved" => "There is no site at that address. Check the spelling.",
            "Timeout" => "The site took too long to answer.",
            "Disconnected" or "CannotConnect" or "ConnectionAborted" or "ConnectionReset" => "The site couldn't be reached. Check the internet connection.",
            "CertificateCommonNameIsIncorrect" or "CertificateExpired" or "CertificateRevoked" or "CertificateIsInvalid" or "ClientCertificateContainsErrors"
                => "The site's security certificate isn't right, so it wasn't opened.",
            "OperationCanceled" => "Opening the page was stopped.",
            "" => "The page didn't open.",
            _ => $"The page didn't open ({browserSaid}).",
        };
    }

    /// <summary>
    /// Takes a picture of the page a browser has open: lays it out at the screen's size, scrolls it through once
    /// so late pictures load, takes the picture, and puts the browser back as it was whatever happens.
    /// </summary>
    /// <param name="devTools">Calls one of the browser's developer tools methods with JSON, and gives its JSON answer.</param>
    /// <param name="mobile">True to lay the page out as a phone or tablet's browser does.</param>
    public static async Task<PageShot> ShootAsync(Func<string, string, Task<string>> devTools, ShotWidth screen, bool wholePage, bool mobile = false)
    {
        await devTools("Emulation.setDeviceMetricsOverride", ScreenMessage(screen.Width, screen.Height, mobile));
        try
        {
            // The page needs a moment to lay itself out again at the new width.
            await Task.Delay(350);
            var pageHeight = PageHeight(await devTools("Runtime.evaluate", ScrollMessage)) ?? screen.Height;
            var (height, cut) = Height(screen, pageHeight, wholePage);
            var png = Picture(await devTools("Page.captureScreenshot", PictureMessage(screen.Width, height)))
                ?? throw new InvalidDataException("The browser gave no picture of that page.");
            return new PageShot(png, screen.Width, height, cut);
        }
        finally
        {
            await devTools("Emulation.clearDeviceMetricsOverride", "{}");
        }
    }

    // What is passed to the browser's developer tools. Kept here so it can be tested without a browser.

    /// <summary>Makes the browser lay the page out as on a screen this size.</summary>
    public static string ScreenMessage(int width, int height, bool mobile = false) =>
        Json(new Dictionary<string, object> { ["width"] = width, ["height"] = height, ["deviceScaleFactor"] = 1, ["mobile"] = mobile });

    /// <summary>Takes the picture from the top of the page down, past what fits on the screen.</summary>
    public static string PictureMessage(int width, int height) =>
        Json(new Dictionary<string, object>
        {
            ["format"] = "png",
            ["captureBeyondViewport"] = true,
            ["clip"] = new Dictionary<string, object> { ["x"] = 0, ["y"] = 0, ["width"] = width, ["height"] = height, ["scale"] = 1 },
        });

    /// <summary>
    /// Scrolls to the bottom and back so pictures that only load when scrolled to are there, then gives the
    /// page's height. Stops after 10 seconds or 40 000 pixels, for pages that never end.
    /// </summary>
    public static string ScrollMessage { get; } = Json(new Dictionary<string, object>
    {
        ["expression"] = """
            (async () => {
              const height = () => Math.max(document.documentElement.scrollHeight, document.body ? document.body.scrollHeight : 0);
              const started = Date.now();
              for (let y = 0; y < height() && y < 40000 && Date.now() - started < 10000; y += Math.max(200, window.innerHeight)) {
                window.scrollTo(0, y);
                await new Promise(r => setTimeout(r, 120));
              }
              window.scrollTo(0, 0);
              await new Promise(r => setTimeout(r, 250));
              return height();
            })()
            """,
        ["awaitPromise"] = true,
        ["returnByValue"] = true,
    });

    /// <summary>The page's height from the answer to <see cref="ScrollMessage"/>. Null when it gave none.</summary>
    public static double? PageHeight(string? answer)
    {
        try
        {
            using var json = JsonDocument.Parse(answer ?? "");
            return json.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The PNG from the answer to <see cref="PictureMessage"/>. Null when there is none.</summary>
    public static byte[]? Picture(string? answer)
    {
        try
        {
            using var json = JsonDocument.Parse(answer ?? "");
            return json.RootElement.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } text
                ? Convert.FromBase64String(text) : null;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return null;
        }
    }

    private static string Json(Dictionary<string, object> value) => JsonSerializer.Serialize(value);
}
