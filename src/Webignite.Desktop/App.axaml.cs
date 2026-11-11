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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // A date box whose date is taken away (another project or task opened, a field cleared) keeps showing the
        // old date's text; clear it once the change has landed.
        Avalonia.Controls.CalendarDatePicker.SelectedDateProperty.Changed.AddClassHandler<Avalonia.Controls.CalendarDatePicker>((picker, e) =>
        {
            if (e.NewValue is null)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (picker.SelectedDate is null)
                        picker.Text = string.Empty;
                });
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Services is null)
            {
                ApplyRestoredBackup();
                Services = new AppServices(LocalStore.OpenDefault());
            }
            MainViewModel.AskToSignInAtStart = true;
            var main = new MainViewModel(Services);
            var window = new MainWindow { DataContext = main };
            main.WatchMail();
            desktop.MainWindow = window;
            var services = Services;
            window.Opened += (_, _) =>
            {
                // Started from a picture in Explorer: show it. Later starts hand theirs over here.
                if (StartPicture is { } picture)
                    services.Go.Picture(picture);
                SingleCopy.Listen(opening => OpenedAgain(window, services, opening));
            };
            desktop.Exit += (_, _) =>
            {
                SingleCopy.Release();
                Services.Dispose();
                // The database is closed now, so the new copy of the app can swap in a restored backup.
                if (RestartAfterExit && Environment.ProcessPath is { } exe)
                    System.Diagnostics.Process.Start(exe);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>A picture the app was started with, e.g. double-clicked in Explorer, shown once the window is up.</summary>
    public static string? StartPicture { get; set; }

    // Webignite started again while running: come to the front, and show the picture it was opened with.
    private static void OpenedAgain(MainWindow window, AppServices services, string opening)
    {
        if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
            window.WindowState = Avalonia.Controls.WindowState.Normal;
        window.Activate();
        if (Webignite.Core.Files.PictureAssociation.PictureFrom([opening], File.Exists) is { } picture)
            services.Go.Picture(picture);
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
        try
        {
            RestoredAtStart = Backups.ApplyPending(root, key => { if (OperatingSystem.IsWindows()) VaultKey.Save(root, key); }, DateTimeOffset.Now, VaultKey.Unprotect);
        }
        catch (Exception e)
        {
            // The restore stays staged and the current data is untouched, so start as normal and say why. The note
            // is posted now and shown once the window is up.
            Background.Report("Finishing the restore", e);
        }
    }
}
