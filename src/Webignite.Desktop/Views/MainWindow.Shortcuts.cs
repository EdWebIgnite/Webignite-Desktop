using Webignite.Core.Tools;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.Views;

/// <summary>
/// The shortcuts that work from any program, all held in one place. Each part of the window says what its
/// shortcut does with <see cref="OnShortcut"/>; this takes the keys from Windows when the window opens, again
/// when Settings changes them, and gives them back when it closes. What differs between shortcuts (the number
/// Windows knows each by, its name, where in Settings it is picked) is in <see cref="GlobalShortcuts.All"/>.
/// Windows only.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<GlobalShortcut, Action> shortcutActions = [];
    private readonly Dictionary<GlobalShortcut, GlobalHotkey> shortcutKeys = [];

    /// <summary>What pressing a shortcut does. Called while the window is being wired, before it opens.</summary>
    private void OnShortcut(GlobalShortcut which, Action pressed) => shortcutActions[which] = pressed;

    private void WireShortcuts(AppServices services)
    {
        services.ApplyShortcut = which => ApplyShortcut(services, which);
        Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows())
                return;
            foreach (var info in GlobalShortcuts.All)
            {
                if (!shortcutActions.TryGetValue(info.Shortcut, out var pressed))
                    continue;
                shortcutKeys[info.Shortcut] = new GlobalHotkey(this, info.HotkeyId, pressed, info.Place);
                if (ApplyShortcut(services, info.Shortcut) is { } problem)
                    ShowNote(info.Title, problem, () => services.Go.Settings(info.Section));
            }
        };
        Closed += (_, _) =>
        {
            if (!OperatingSystem.IsWindows())
                return;
            foreach (var key in shortcutKeys.Values)
                key.Release();
            shortcutKeys.Clear();
        };
    }

    private string? ApplyShortcut(AppServices services, GlobalShortcut which)
    {
        if (!OperatingSystem.IsWindows() || !shortcutKeys.TryGetValue(which, out var key))
            return null;
        var saved = GlobalShortcuts.Keys(which, services.Settings.Screenshots, services.Settings.Notes.QuickShortcut);
        if (string.IsNullOrWhiteSpace(saved))
            return key.Set(null);
        var (keys, problem) = Hotkey.Parse(saved);
        return problem ?? key.Set(keys);
    }
}
