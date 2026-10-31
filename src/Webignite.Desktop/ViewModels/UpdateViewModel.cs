using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Velopack;
using Velopack.Sources;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Looks for a newer version on GitHub when the app starts, downloads it quietly, and offers a restart.
/// Your data lives in a separate folder, so updating never touches it.
/// </summary>
public partial class UpdateViewModel : ViewModelBase
{
    public const string Repository = "https://github.com/EdWebIgnite/Webignite-Desktop";

    private UpdateManager? manager;
    private UpdateInfo? ready;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    public partial string? ReadyVersion { get; set; }

    public bool IsReady => ReadyVersion is not null;

    public string VersionText { get; private set; } = "Test build";

    public async Task CheckAsync()
    {
        try
        {
            manager = new UpdateManager(new GithubSource(Repository, null, false));
            if (!manager.IsInstalled)
                return; // Running from an unzipped folder or in development: nothing to update.
            VersionText = "Version " + manager.CurrentVersion;
            OnPropertyChanged(nameof(VersionText));
            if (await manager.CheckForUpdatesAsync() is not { } update)
                return;
            await manager.DownloadUpdatesAsync(update);
            ready = update;
            ReadyVersion = update.TargetFullRelease.Version.ToString();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // Offline or GitHub unreachable: try again next start.
        }
    }

    [RelayCommand]
    private void Restart()
    {
        if (manager is not null && ready is not null)
            manager.ApplyUpdatesAndRestart(ready);
    }
}
