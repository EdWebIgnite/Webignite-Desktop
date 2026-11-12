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
    [ObservableProperty] public partial string SnippetShortcut { get; private set; } = "";
    [ObservableProperty] public partial string TextShortcut { get; private set; } = "";
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
        foreach (var info in GlobalShortcuts.All.Where(i => i.Shortcut != GlobalShortcut.QuickNote))
            ShowShortcut(info.Shortcut);
    }

    /// <summary>Why these keys can't be used for this shortcut, when another one has them.</summary>
    private string? Clash(Hotkey keys, GlobalShortcut wanted) =>
        GlobalShortcuts.TakenBy(keys, services.Settings.Screenshots, services.Settings.Notes.QuickShortcut, wanted) is { } taken
            ? GlobalShortcuts.Clash(taken, wanted)
            : null;

    /// <summary>
    /// The keys pressed in a shortcut's box. Kept and taken from Windows straight away, or refused with why.
    /// The same for every shortcut; what differs is in <see cref="GlobalShortcuts.All"/>.
    /// </summary>
    public void SetShortcut(GlobalShortcut which, string keys)
    {
        var (hotkey, problem) = Hotkey.Parse(keys);
        if (hotkey is null)
        {
            SayShortcut(which, problem!, true);
            return;
        }
        if (Clash(hotkey, which) is { } clash)
        {
            SayShortcut(which, clash, true);
            return;
        }
        KeepShortcut(which, hotkey.ToString());
        var taken = services.ApplyShortcut?.Invoke(which);
        SayShortcut(which, taken ?? GlobalShortcuts.SetHint(which, hotkey), taken is not null);
    }

    [RelayCommand]
    private void ShortcutOff(GlobalShortcut which)
    {
        KeepShortcut(which, "");
        services.ApplyShortcut?.Invoke(which);
        SayShortcut(which, GlobalShortcuts.Info(which).OffHint, false);
    }

    /// <summary>Back to the keys the shortcut came with.</summary>
    [RelayCommand]
    private void ShortcutBack(GlobalShortcut which) => SetShortcut(which, GlobalShortcuts.Info(which).DefaultKeys);

    private void KeepShortcut(GlobalShortcut which, string keys)
    {
        if (which == GlobalShortcut.QuickNote)
            services.Settings.Notes = services.Settings.Notes with { QuickShortcut = keys };
        else
            services.Settings.Screenshots = GlobalShortcuts.With(services.Settings.Screenshots, which, keys);
        ShowShortcut(which);
    }

    /// <summary>Puts a shortcut's saved keys, or "Off", in its box.</summary>
    private void ShowShortcut(GlobalShortcut which)
    {
        var keys = GlobalShortcuts.Keys(which, services.Settings.Screenshots, services.Settings.Notes.QuickShortcut);
        var shown = keys.Length > 0 ? keys : "Off";
        switch (which)
        {
            case GlobalShortcut.Screenshot: ScreenshotShortcut = shown; break;
            case GlobalShortcut.Recording: RecordShortcut = shown; break;
            case GlobalShortcut.QuickNote: QuickNoteShortcut = shown; break;
            case GlobalShortcut.ColourPicker: ColourShortcut = shown; break;
            case GlobalShortcut.ClipboardHistory: ClipboardShortcut = shown; break;
            case GlobalShortcut.Ruler: RulerShortcut = shown; break;
            case GlobalShortcut.Snippets: SnippetShortcut = shown; break;
            case GlobalShortcut.ScreenText: TextShortcut = shown; break;
            default: throw new ArgumentOutOfRangeException(nameof(which), which, null);
        }
    }

    // The quick note's box is in Settings, Notes, with its own line for messages.
    private void SayShortcut(GlobalShortcut which, string message, bool problem)
    {
        if (which == GlobalShortcut.QuickNote)
            QuickNoteSay(message, problem);
        else
            Shortcut(message, problem);
    }

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
