using Avalonia;
using Velopack;

namespace Webignite.Desktop;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Lets the installer and updater do their part (shortcuts, clean upgrades) before the window opens.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
}
