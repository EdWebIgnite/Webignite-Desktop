using System.Runtime.InteropServices;
using Avalonia;
using Velopack;
using Webignite.Data;

namespace Webignite.Desktop;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Lets the installer and updater do their part (shortcuts, clean upgrades) before the window opens.
        VelopackApp.Build().Run();
        WatchForCrashes();
        UseSouthAfricanEnglish();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            ReportCrash("Webignite stopped", e);
            throw;
        }
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Anything that would otherwise close the app without a word is written to the log, and the person is told
    /// where to look. Background tasks nobody awaited are logged too, so a failed check leaves a trace. Errors on
    /// the screen's thread don't close the app at all: see <see cref="Background.CatchUnhandled"/>.
    /// </summary>
    private static void WatchForCrashes()
    {
        AppLog.Trim(DateTimeOffset.Now);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
                ReportCrash("Webignite stopped", error);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write("A background task failed and nobody was waiting for it", e.Exception);
            e.SetObserved();
        };
        // An error on the screen's thread (a button, a timer, a handler) is logged and shown, and the app carries on.
        // Only an error outside it, or before the window exists, still closes the app with the message below.
        Background.CatchUnhandled();
    }

    /// <summary>
    /// Dates and times on screen read the same on every PC: "4 November 2026, 14:30", whatever language or region
    /// Windows is set to. Text for clients and Xero is fixed in Core itself and doesn't rely on this.
    /// </summary>
    private static void UseSouthAfricanEnglish()
    {
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("en-ZA");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
        catch (System.Globalization.CultureNotFoundException e)
        {
            AppLog.Write("South African English isn't available on this PC; using the Windows setting", e);
        }
    }

    private static bool crashReported;

    private static void ReportCrash(string what, Exception error)
    {
        if (crashReported)
            return;
        crashReported = true;
        AppLog.Write(what, error);
        if (OperatingSystem.IsWindows())
        {
            var text = $"Something went wrong and Webignite has to close.\n\nWhat happened is written in\n{AppLog.CurrentFile(DateTimeOffset.Now)}\n\n{error.GetType().Name}: {error.Message}";
            MessageBoxW(IntPtr.Zero, text, "Webignite", 0x10 /* MB_ICONERROR */);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
