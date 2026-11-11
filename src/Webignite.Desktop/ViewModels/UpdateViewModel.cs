using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Velopack;
using Velopack.Sources;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Looks for a newer version on GitHub when the app starts and then every hour (or at once from Check for
/// updates under the version), downloads it quietly, and offers a restart.
/// Your data lives in a separate folder, so updating never touches it.
/// </summary>
public partial class UpdateViewModel : ViewModelBase
{
    public const string Repository = Webignite.Core.Accounts.InvitePackage.Releases;

    private UpdateManager? manager;
    private UpdateInfo? ready;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    public partial string? ReadyVersion { get; set; }

    public bool IsReady => ReadyVersion is not null;

    public string VersionText { get; private set; } = "Test build";

    /// <summary>What the last check found, shown under the version for a few seconds after Check for updates.</summary>
    [ObservableProperty] public partial string CheckNote { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    public partial bool Checking { get; private set; }

    /// <summary>When it last looked; the background look runs once an hour after the one at start.</summary>
    public DateTimeOffset LastCheck { get; private set; } = DateTimeOffset.MinValue;

    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    public bool CheckDue(DateTimeOffset now) => !IsReady && !Checking && now - LastCheck >= CheckEvery;

    /// <summary>Installed with Setup, so it can update itself; a test build or unzipped copy can't, and hides the link.</summary>
    [ObservableProperty] public partial bool CanUpdate { get; private set; }

    private enum Found { Nothing, Ready, NotInstalled, Unreachable }

    /// <summary>Looks for a newer version and downloads it quietly, saying nothing if it can't (tried again within the hour).</summary>
    public async Task CheckAsync() => await Look();

    private async Task<Found> Look()
    {
        if (Checking)
            return IsReady ? Found.Ready : Found.Nothing;
        Checking = true;
        LastCheck = DateTimeOffset.Now;
        try
        {
            manager ??= new UpdateManager(new GithubSource(Repository, null, false));
            if (!manager.IsInstalled)
                return Found.NotInstalled; // Running from an unzipped folder or in development: nothing to update.
            CanUpdate = true;
            VersionText = "Version " + manager.CurrentVersion;
            OnPropertyChanged(nameof(VersionText));
            if (await manager.CheckForUpdatesAsync() is not { } update)
                return Found.Nothing;
            await manager.DownloadUpdatesAsync(update);
            ready = update;
            ReadyVersion = update.TargetFullRelease.Version.ToString();
            return Found.Ready;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            Webignite.Data.AppLog.Write("Checking for an update", e);
            return Found.Unreachable;
        }
        finally
        {
            Checking = false;
        }
    }

    /// <summary>Check for updates, from the link under the version: says what it found.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNow()
    {
        CheckNote = "Checking…";
        CheckNote = await Look() switch
        {
            Found.Ready => "",
            Found.NotInstalled => "This copy isn't installed, so it can't update itself.",
            Found.Unreachable => "Couldn't reach the update server. Check the internet connection and try again.",
            _ => "You have the newest version.",
        };
    }

    private bool CanCheckNow() => !Checking && !IsReady;

    /// <summary>
    /// Runs just before the update is put in place: whatever runs from the install's folder (the connector Claude
    /// Desktop keeps open, ChatGPT's tunnel) is stopped, or Windows won't let the files be replaced and the old
    /// version opens again.
    /// </summary>
    public Action? BeforeRestart { get; set; }

    [RelayCommand]
    private void Restart()
    {
        if (manager is null || ready is null)
            return;
        BeforeRestart?.Invoke();
        manager.ApplyUpdatesAndRestart(ready);
    }
}
