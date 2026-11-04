using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Settings, Screenshots: the shortcut that works from any program, and where screenshots are saved.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial string ScreenshotShortcut { get; private set; } = "";
    [ObservableProperty] public partial string ShortcutMessage { get; private set; } = "";
    [ObservableProperty] public partial bool ShortcutProblem { get; private set; }

    public string ScreenshotFolder => services.Settings.Screenshots.Folder is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    [ObservableProperty] public partial string RecordShortcut { get; private set; } = "";

    /// <summary>Record the microphone with the screen.</summary>
    public bool RecordMicrophone
    {
        get => services.Settings.Screenshots.Microphone;
        set
        {
            services.Settings.Screenshots = services.Settings.Screenshots with { Microphone = value };
            OnPropertyChanged();
        }
    }

    private void LoadScreenshots()
    {
        ScreenshotShortcut = services.Settings.Screenshots.Shortcut is { Length: > 0 } keys ? keys : "Off";
        RecordShortcut = services.Settings.Screenshots.RecordShortcut is { Length: > 0 } record ? record : "Off";
    }

    /// <summary>The keys pressed in the recording shortcut box.</summary>
    public void SetRecordShortcut(string keys)
    {
        var (hotkey, problem) = Hotkey.Parse(keys);
        if (hotkey is null)
        {
            Shortcut(problem!, true);
            return;
        }
        if (hotkey.ToString() == services.Settings.Screenshots.Shortcut)
        {
            Shortcut("That's the screenshot shortcut already. Pick other keys for recording.", true);
            return;
        }
        services.Settings.Screenshots = services.Settings.Screenshots with { RecordShortcut = hotkey.ToString() };
        RecordShortcut = hotkey.ToString();
        var taken = services.ApplyRecordShortcut?.Invoke();
        Shortcut(taken ?? $"Press {hotkey} in any program to start recording, and again to stop.", taken is not null);
    }

    /// <summary>The keys pressed in the shortcut box. Kept and taken from Windows straight away, or refused with why.</summary>
    public void SetShortcut(string keys)
    {
        var (hotkey, problem) = Hotkey.Parse(keys);
        if (hotkey is null)
        {
            Shortcut(problem!, true);
            return;
        }
        if (hotkey.ToString() == services.Settings.Screenshots.RecordShortcut)
        {
            Shortcut("That's the recording shortcut already. Pick other keys for screenshots.", true);
            return;
        }
        services.Settings.Screenshots = services.Settings.Screenshots with { Shortcut = hotkey.ToString() };
        ScreenshotShortcut = hotkey.ToString();
        var taken = services.ApplyScreenshotShortcut?.Invoke();
        Shortcut(taken ?? $"Press {hotkey} in any program to take a screenshot.", taken is not null);
    }

    [RelayCommand]
    private void ShortcutOff()
    {
        services.Settings.Screenshots = services.Settings.Screenshots with { Shortcut = "" };
        ScreenshotShortcut = "Off";
        services.ApplyScreenshotShortcut?.Invoke();
        Shortcut("The shortcut is off. Take a screenshot from the Mark-up page.", false);
    }

    [RelayCommand]
    private void ShortcutBack() => SetShortcut(ScreenshotSettings.DefaultShortcut);

    public void SetScreenshotFolder(string folder)
    {
        services.Settings.Screenshots = services.Settings.Screenshots with { Folder = folder };
        OnPropertyChanged(nameof(ScreenshotFolder));
        Say("Screenshots without a client go to " + folder + ".");
    }

    private void Shortcut(string message, bool problem)
    {
        ShortcutMessage = message;
        ShortcutProblem = problem;
    }
}
