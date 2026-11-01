using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Webignite.Data;
using Webignite.Desktop.ViewModels;
using Webignite.Desktop.Views;

namespace Webignite.Desktop;

public partial class App : Application
{
    /// <summary>Set before start-up to use another database, e.g. by the screenshot tool.</summary>
    public static AppServices? Services { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Services is null)
            {
                ApplyRestoredBackup();
                Services = new AppServices(LocalStore.OpenDefault());
            }
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(Services) };
            desktop.Exit += (_, _) =>
            {
                Services.Dispose();
                // The database is closed now, so the new copy of the app can swap in a restored backup.
                if (RestartAfterExit && Environment.ProcessPath is { } exe)
                    System.Diagnostics.Process.Start(exe);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Starts the app again once this copy has closed its database.</summary>
    public static bool RestartAfterExit { get; set; }

    /// <summary>Set when a backup was swapped in at start-up, so Settings can say so.</summary>
    public static bool RestoredAtStart { get; private set; }

    // A restore is staged by Settings and only swapped in here, before the database is opened.
    private static void ApplyRestoredBackup()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = DataPaths.Root();
        RestoredAtStart = Backups.ApplyPending(root, key => { if (OperatingSystem.IsWindows()) VaultKey.Save(root, key); }, DateTimeOffset.Now);
    }
}
