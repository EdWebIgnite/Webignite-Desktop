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
    [ObservableProperty] public partial string ColourShortcut { get; private set; } = "";
    [ObservableProperty] public partial string RulerShortcut { get; private set; } = "";
    [ObservableProperty] public partial string ClipboardShortcut { get; private set; } = "";

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
        ColourShortcut = services.Settings.Screenshots.ColourShortcut is { Length: > 0 } colour ? colour : "Off";
        ClipboardShortcut = services.Settings.Screenshots.ClipboardShortcut is { Length: > 0 } clips ? clips : "Off";
        RulerShortcut = services.Settings.Screenshots.RulerShortcut is { Length: > 0 } ruler ? ruler : "Off";
    }

    /// <summary>Why these keys can't be used for this shortcut, when another one has them.</summary>
    private string? Clash(Hotkey keys, GlobalShortcut wanted) =>
        GlobalShortcuts.TakenBy(keys, services.Settings.Screenshots, services.Settings.Notes.QuickShortcut, wanted) is { } taken
            ? GlobalShortcuts.Clash(taken, wanted)
            : null;

    /// <summary>The keys pressed in the colour picker shortcut box.</summary>
    public void SetColourShortcut(string keys)
    {
        var (hotkey, problem) = Hotkey.Parse(keys);
        if (hotkey is null)
        {
            Shortcut(problem!, true);
            return;
        }
        if (Clash(hotkey, GlobalShortcut.ColourPicker) is { } clash)
        {
            Shortcut(clash, true);
            return;
        }
        services.Settings.Screenshots = services.Settings.Screenshots with { ColourShortcut = hotkey.ToString() };
        ColourShortcut = hotkey.ToString();
        var taken = services.ApplyColourShortcut?.Invoke();
        Shortcut(taken ?? $"Press {hotkey} in any program, then click a colour to copy its code.", taken is not null);
    }

    /// <summary>The keys pressed in the screen ruler shortcut box.</summary>
    public void SetRulerShortcut(string keys)
    {
        var (hotkey, problem) = Hotkey.Parse(keys);
        if (hotkey is null)
        {
            Shortcut(problem!, true);
            return;
        }
        if (Clash(hotkey, GlobalShortcut.Ruler) is { } clash)
        {
            Shortcut(clash, true);
            return;
        }
        services.Settings.Screenshots = services.Settings.Screenshots with { RulerShortcut = hotkey.ToString() };
        RulerShortcut = hotkey.ToString();
        var taken = services.ApplyRulerShortcut?.Invoke();
        Shortcut(taken ?? $"Press {hotkey} in any program, then point at something or drag a box to measure it.", taken is not null);
    }

    [RelayCommand]
    private void RulerShortcutOff()
    {
        services.Settings.Screenshots = services.Settings.Screenshots with { RulerShortcut = "" };
        RulerShortcut = "Off";
        services.ApplyRulerShortcut?.Invoke();
        Shortcut("The screen ruler shortcut is off. Measure from the Screen ruler tool.", false);
    }

    /// <summary>The keys pressed in the clipboard history shortcut box.</summary>
    public void SetClipboardShortcut(string keys)
    {
        var (hotkey, problem) = Hotkey.Parse(keys);
        if (hotkey is null)
        {
            Shortcut(problem!, true);
            return;
        }
        if (Clash(hotkey, GlobalShortcut.ClipboardHistory) is { } clash)
        {
            Shortcut(clash, true);
            return;
        }
        services.Settings.Screenshots = services.Settings.Screenshots with { ClipboardShortcut = hotkey.ToString() };
        ClipboardShortcut = hotkey.ToString();
        var taken = services.ApplyClipboardShortcut?.Invoke();
        Shortcut(taken ?? $"Press {hotkey} in any program to see what you copied.", taken is not null);
    }

    [RelayCommand]
    private void ClipboardShortcutOff()
    {
        services.Settings.Screenshots = services.Settings.Screenshots with { ClipboardShortcut = "" };
        ClipboardShortcut = "Off";
        services.ApplyClipboardShortcut?.Invoke();
        Shortcut("The clipboard history shortcut is off. Open it from the sidebar under Computer.", false);
    }

    [RelayCommand]
    private void ColourShortcutOff()
    {
        services.Settings.Screenshots = services.Settings.Screenshots with { ColourShortcut = "" };
        ColourShortcut = "Off";
        services.ApplyColourShortcut?.Invoke();
        Shortcut("The colour picker shortcut is off. Pick colours from the Colours tool.", false);
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
        if (Clash(hotkey, GlobalShortcut.Recording) is { } clash)
        {
            Shortcut(clash, true);
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
        if (Clash(hotkey, GlobalShortcut.Screenshot) is { } clash)
        {
            Shortcut(clash, true);
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
