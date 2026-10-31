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
            Services ??= new AppServices(LocalStore.OpenDefault());
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(Services) };
            desktop.Exit += (_, _) => Services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
