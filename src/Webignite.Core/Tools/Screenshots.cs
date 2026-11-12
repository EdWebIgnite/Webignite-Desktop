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

    public const string DefaultSnippetShortcut = "Ctrl+Alt+T";

    /// <summary>The keys that open the text snippets from any program, to paste one where you were typing. Empty turns it off.</summary>
    public string SnippetShortcut { get; init; } = DefaultSnippetShortcut;

    public const string DefaultTextShortcut = "Ctrl+Alt+X";

    /// <summary>The keys that copy the words out of an area of the screen. Empty turns it off.</summary>
    public string TextShortcut { get; init; } = DefaultTextShortcut;
}

/// <summary>The shortcuts that work from any program, so one can't be given keys another already has.</summary>
public enum GlobalShortcut { Screenshot, Recording, QuickNote, ColourPicker, ClipboardHistory, Ruler, Snippets, ScreenText }

/// <summary>
/// Everything that differs between one global shortcut and the next, in one row: what Windows knows it by, what it
/// is called in messages, its usual keys, where in Settings it is picked, and what Settings says when it is set or
/// turned off. Adding a shortcut is a new name in <see cref="GlobalShortcut"/> and a row here.
/// </summary>
/// <param name="HotkeyId">The number Windows hands back when the keys are pressed. Never reuse or change one.</param>
/// <param name="Name">As in "That's the {Name} shortcut already".</param>
/// <param name="For">As in "Pick other keys for {For}".</param>
/// <param name="Title">The note's heading when Windows won't hand the keys over at start-up.</param>
/// <param name="Section">The part of Settings the keys are picked in, as its key.</param>
/// <param name="Place">The same part of Settings, as the person reads it.</param>
/// <param name="SetHint">What Settings says once the keys are taken; {0} is the keys.</param>
/// <param name="OffHint">What Settings says once the shortcut is off, with where else to start it.</param>
public sealed record GlobalShortcutInfo(GlobalShortcut Shortcut, int HotkeyId, string Name, string For, string Title,
    string Section, string Place, string DefaultKeys, string SetHint, string OffHint);

public static class GlobalShortcuts
{
    public static readonly IReadOnlyList<GlobalShortcutInfo> All =
    [
        new(GlobalShortcut.Screenshot, 0x5747, "screenshot", "screenshots", "Screenshot shortcut not set", "screenshots", "Screenshots",
            ScreenshotSettings.DefaultShortcut, "Press {0} in any program to take a screenshot.",
            "The shortcut is off. Take a screenshot from the Mark-up page."),
        new(GlobalShortcut.Recording, 0x5748, "recording", "recording", "Recording shortcut not set", "screenshots", "Screenshots",
            ScreenshotSettings.DefaultRecordShortcut, "Press {0} in any program to start recording, and again to stop.",
            "The recording shortcut is off. Start a recording from the Mark-up page."),
        new(GlobalShortcut.QuickNote, 0x5749, "quick-note", "the quick note", "Quick note shortcut not set", "notes", "Notes",
            Webignite.Core.Notes.NoteSettings.DefaultShortcut, "Press {0} in any program to jot down a quick note.",
            "The shortcut is off. Write notes on the Notes page."),
        new(GlobalShortcut.ColourPicker, 0x574A, "colour picker", "the colour picker", "Colour picker shortcut not set", "screenshots", "Screenshots",
            ScreenshotSettings.DefaultColourShortcut, "Press {0} in any program, then click a colour to copy its code.",
            "The colour picker shortcut is off. Pick colours from the Colours tool."),
        new(GlobalShortcut.ClipboardHistory, 0x574B, "clipboard history", "clipboard history", "Clipboard history shortcut not set", "screenshots", "Screenshots",
            ScreenshotSettings.DefaultClipboardShortcut, "Press {0} in any program to see what you copied.",
            "The clipboard history shortcut is off. Open it from the sidebar under Computer."),
        new(GlobalShortcut.Ruler, 0x574C, "screen ruler", "the screen ruler", "Screen ruler shortcut not set", "screenshots", "Screenshots",
            ScreenshotSettings.DefaultRulerShortcut, "Press {0} in any program, then point at something or drag a box to measure it.",
            "The screen ruler shortcut is off. Measure from the Screen ruler tool."),
        new(GlobalShortcut.Snippets, 0x574D, "text snippets", "text snippets", "Text snippets shortcut not set", "screenshots", "Screenshots",
            ScreenshotSettings.DefaultSnippetShortcut, "Press {0} in any program, pick a snippet, and it is pasted where you were typing.",
            "The text snippets shortcut is off. Open them from the sidebar under Computer."),
        new(GlobalShortcut.ScreenText, 0x574E, "text from the screen", "text from the screen", "Text from the screen shortcut not set", "screenshots", "Screenshots",
            ScreenshotSettings.DefaultTextShortcut, "Press {0} in any program, then drag over the words to copy them.",
            "The text-from-the-screen shortcut is off. Use the Text from the screen tool."),
    ];

    /// <summary>The row for a shortcut. One with no row is a mistake in the table, so it stops here and not in a wrong message.</summary>
    public static GlobalShortcutInfo Info(GlobalShortcut shortcut) =>
        All.FirstOrDefault(i => i.Shortcut == shortcut) ?? throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, "This shortcut has no row in GlobalShortcuts.All.");

    public static string Name(GlobalShortcut shortcut) => Info(shortcut).Name;

    /// <summary>The keys saved for a shortcut; empty when it is off. The quick note's are kept with the notes settings.</summary>
    public static string Keys(GlobalShortcut shortcut, ScreenshotSettings shots, string quickNote) => shortcut switch
    {
        GlobalShortcut.Screenshot => shots.Shortcut,
        GlobalShortcut.Recording => shots.RecordShortcut,
        GlobalShortcut.QuickNote => quickNote,
        GlobalShortcut.ColourPicker => shots.ColourShortcut,
        GlobalShortcut.ClipboardHistory => shots.ClipboardShortcut,
        GlobalShortcut.Ruler => shots.RulerShortcut,
        GlobalShortcut.Snippets => shots.SnippetShortcut,
        GlobalShortcut.ScreenText => shots.TextShortcut,
        _ => throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, null),
    };

    /// <summary>The settings with one shortcut's keys changed. Not for the quick note, whose keys are kept with the notes settings.</summary>
    public static ScreenshotSettings With(ScreenshotSettings shots, GlobalShortcut shortcut, string keys) => shortcut switch
    {
        GlobalShortcut.Screenshot => shots with { Shortcut = keys },
        GlobalShortcut.Recording => shots with { RecordShortcut = keys },
        GlobalShortcut.ColourPicker => shots with { ColourShortcut = keys },
        GlobalShortcut.ClipboardHistory => shots with { ClipboardShortcut = keys },
        GlobalShortcut.Ruler => shots with { RulerShortcut = keys },
        GlobalShortcut.Snippets => shots with { SnippetShortcut = keys },
        GlobalShortcut.ScreenText => shots with { TextShortcut = keys },
        _ => throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, null),
    };

    /// <summary>The other shortcut that has these keys already, or null when they're free.</summary>
    public static GlobalShortcut? TakenBy(Hotkey keys, ScreenshotSettings shots, string quickNote, GlobalShortcut self)
    {
        var wanted = keys.ToString();
        foreach (var info in All)
        {
            if (info.Shortcut != self && string.Equals(Keys(info.Shortcut, shots, quickNote), wanted, StringComparison.OrdinalIgnoreCase))
                return info.Shortcut;
        }
        return null;
    }

    /// <summary>"That's the screenshot shortcut already. Pick other keys for recording."</summary>
    public static string Clash(GlobalShortcut taken, GlobalShortcut wanted) =>
        $"That's the {Name(taken)} shortcut already. Pick other keys for {Info(wanted).For}.";

    /// <summary>What Settings says once the keys are taken.</summary>
    public static string SetHint(GlobalShortcut shortcut, Hotkey keys) =>
        string.Format(CultureInfo.InvariantCulture, Info(shortcut).SetHint, keys);
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
