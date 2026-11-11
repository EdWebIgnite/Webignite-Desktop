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

    /// <summary>Looks for a newer version and downloads it quietly; true when one is ready to restart into.</summary>
    public async Task<bool> CheckAsync()
    {
        if (Checking)
            return IsReady;
        Checking = true;
        LastCheck = DateTimeOffset.Now;
        try
        {
            manager ??= new UpdateManager(new GithubSource(Repository, null, false));
            if (!manager.IsInstalled)
                return false; // Running from an unzipped folder or in development: nothing to update.
            VersionText = "Version " + manager.CurrentVersion;
            OnPropertyChanged(nameof(VersionText));
            if (await manager.CheckForUpdatesAsync() is not { } update)
                return false;
            await manager.DownloadUpdatesAsync(update);
            ready = update;
            ReadyVersion = update.TargetFullRelease.Version.ToString();
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // Offline or GitHub unreachable: tried again within the hour.
            Webignite.Data.AppLog.Write("Checking for an update", e);
            CheckNote = "Couldn't reach the update server. Check the internet connection and try again.";
            return false;
        }
        finally
        {
            Checking = false;
        }
    }

    /// <summary>Check for updates, from the link under the version.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNow()
    {
        CheckNote = "Checking…";
        var found = await CheckAsync();
        if (found)
            CheckNote = "";
        else if (CheckNote == "Checking…")
            CheckNote = manager?.IsInstalled == false ? "This copy isn't installed, so it can't update itself." : "You have the newest version.";
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
