using System.Globalization;

namespace Webignite.Core.Tools;

/// <summary>What the screenshot shortcut is and where screenshots are saved.</summary>
public sealed record ScreenshotSettings
{
    public const string DefaultShortcut = "Ctrl+Alt+S";

    /// <summary>The keys that start a screenshot from any program, e.g. "Ctrl+Alt+S". Empty turns the shortcut off.</summary>
    public string Shortcut { get; init; } = DefaultShortcut;

    /// <summary>Where screenshots without a client go. Empty means Pictures\Screenshots.</summary>
    public string? Folder { get; init; }

    public const string DefaultRecordShortcut = "Ctrl+Alt+R";

    /// <summary>The keys that start a screen recording from any program, and stop it again. Empty turns it off.</summary>
    public string RecordShortcut { get; init; } = DefaultRecordShortcut;

    /// <summary>Record the microphone with the screen.</summary>
    public bool Microphone { get; init; } = true;

    public const string DefaultColourShortcut = "Ctrl+Alt+C";

    /// <summary>The keys that pick a colour from anywhere on the screen. Empty turns it off.</summary>
    public string ColourShortcut { get; init; } = DefaultColourShortcut;

    public const string DefaultClipboardShortcut = "Ctrl+Alt+V";

    /// <summary>The keys that open clipboard history from any program. Empty turns it off.</summary>
    public string ClipboardShortcut { get; init; } = DefaultClipboardShortcut;

    public const string DefaultRulerShortcut = "Ctrl+Alt+M";

    /// <summary>The keys that measure anything on the screen. Empty turns it off.</summary>
    public string RulerShortcut { get; init; } = DefaultRulerShortcut;
}

/// <summary>The shortcuts that work from any program, so one can't be given keys another already has.</summary>
public enum GlobalShortcut { Screenshot, Recording, QuickNote, ColourPicker, ClipboardHistory, Ruler }

public static class GlobalShortcuts
{
    public static string Name(GlobalShortcut shortcut) => shortcut switch
    {
        GlobalShortcut.Screenshot => "screenshot",
        GlobalShortcut.Recording => "recording",
        GlobalShortcut.QuickNote => "quick-note",
        GlobalShortcut.ClipboardHistory => "clipboard history",
        GlobalShortcut.Ruler => "screen ruler",
        _ => "colour picker",
    };

    /// <summary>The other shortcut that has these keys already, or null when they're free.</summary>
    public static GlobalShortcut? TakenBy(Hotkey keys, ScreenshotSettings shots, string quickNote, GlobalShortcut self)
    {
        var wanted = keys.ToString();
        var all = new (GlobalShortcut Shortcut, string Keys)[]
        {
            (GlobalShortcut.Screenshot, shots.Shortcut),
            (GlobalShortcut.Recording, shots.RecordShortcut),
            (GlobalShortcut.QuickNote, quickNote),
            (GlobalShortcut.ColourPicker, shots.ColourShortcut),
            (GlobalShortcut.ClipboardHistory, shots.ClipboardShortcut),
            (GlobalShortcut.Ruler, shots.RulerShortcut),
        };
        foreach (var (shortcut, taken) in all)
        {
            if (shortcut != self && string.Equals(taken, wanted, StringComparison.OrdinalIgnoreCase))
                return shortcut;
        }
        return null;
    }

    /// <summary>"That's the screenshot shortcut already. Pick other keys for recording."</summary>
    public static string Clash(GlobalShortcut taken, GlobalShortcut wanted) =>
        $"That's the {Name(taken)} shortcut already. Pick other keys for {For(wanted)}.";

    private static string For(GlobalShortcut shortcut) => shortcut switch
    {
        GlobalShortcut.Screenshot => "screenshots",
        GlobalShortcut.Recording => "recording",
        GlobalShortcut.QuickNote => "the quick note",
        GlobalShortcut.ClipboardHistory => "clipboard history",
        GlobalShortcut.Ruler => "the screen ruler",
        _ => "the colour picker",
    };
}

/// <summary>A key with Ctrl, Alt, Shift or the Windows key, for a shortcut that works from any program.</summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    private static readonly HashSet<string> Alone = new(StringComparer.OrdinalIgnoreCase) { "PrintScreen", "Pause", "ScrollLock", "Insert" };

    /// <summary>"Ctrl+Alt+S", or null with why when it can't be used.</summary>
    public static (Hotkey? Hotkey, string? Problem) Parse(string? text)
    {
        var parts = (text ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return (null, "Press the keys you want, e.g. Ctrl+Alt+S.");
        bool ctrl = false, alt = false, shift = false, win = false;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win" or "windows" or "meta":
                    win = true;
                    break;
                default:
                    return (null, $"{part} isn't Ctrl, Alt, Shift or Win.");
            }
        }
        var key = Normal(parts[^1]);
        if (key is null)
            return (null, $"{parts[^1]} can't be used for a shortcut. Use a letter, a number, F1 to F24 or Print Screen.");
        var function = key.Length > 1 && key[0] == 'F' && int.TryParse(key[1..], out _);
        if (!ctrl && !alt && !win && !function && !Alone.Contains(key))
            return (null, "Add Ctrl, Alt or Win, so the shortcut doesn't fire while you type.");
        return (new Hotkey(ctrl, alt, shift, win, key), null);
    }

    // A letter or digit as itself, F1 to F24, and a few named keys, in the way Windows names them.
    private static string? Normal(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return key.ToUpperInvariant();
        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24)
            return "F" + n.ToString(CultureInfo.InvariantCulture);
        return key.ToLowerInvariant() switch
        {
            "printscreen" or "prtsc" or "prtscn" or "print" or "snapshot" => "PrintScreen",
            "pause" => "Pause",
            "scrolllock" or "scroll" => "ScrollLock",
            "insert" or "ins" => "Insert",
            _ => null,
        };
    }

    public override string ToString() =>
        string.Join('+', new[] { Ctrl ? "Ctrl" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, Win ? "Win" : null, Key }.OfType<string>());
}

/// <summary>Choosing what to grab on a frozen screen: a dragged region, a clicked window, or all of it.</summary>
public static class ScreenGrab
{
    /// <summary>A press and release closer than this is a click (take the window under it), not a drag.</summary>
    public const int ClickSlack = 4;

    public readonly record struct Area(int X, int Y, int Width, int Height)
    {
        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;
    }

    /// <summary>The area a drag covers, from any corner, kept inside the screen.</summary>
    public static Area Region(int x1, int y1, int x2, int y2, Area screen)
    {
        var left = Math.Clamp(Math.Min(x1, x2), screen.X, screen.X + screen.Width);
        var top = Math.Clamp(Math.Min(y1, y2), screen.Y, screen.Y + screen.Height);
        var right = Math.Clamp(Math.Max(x1, x2), screen.X, screen.X + screen.Width);
        var bottom = Math.Clamp(Math.Max(y1, y2), screen.Y, screen.Y + screen.Height);
        return new Area(left, top, right - left, bottom - top);
    }

    public static bool IsClick(int x1, int y1, int x2, int y2) => Math.Abs(x1 - x2) <= ClickSlack && Math.Abs(y1 - y2) <= ClickSlack;

    /// <summary>The window under a point: the first in front-to-back order that holds it, cut to the screen; else the screen.</summary>
    public static Area WindowAt(int x, int y, IEnumerable<Area> frontToBack, Area screen)
    {
        foreach (var window in frontToBack)
        {
            if (!window.Contains(x, y))
                continue;
            var cut = Region(window.X, window.Y, window.X + window.Width, window.Y + window.Height, screen);
            if (!cut.IsEmpty)
                return cut;
        }
        return screen;
    }
}
