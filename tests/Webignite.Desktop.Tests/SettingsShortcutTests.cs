using Webignite.Core.Tools;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Settings: the shortcuts that work from any program are all set, refused and turned off the same way.</summary>
public sealed class SettingsShortcutTests
{
    [Fact]
    public Task Keys_pressed_in_a_box_are_saved_shown_and_taken_from_Windows() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var asked = new List<GlobalShortcut>();
        app.Services.ApplyShortcut = which =>
        {
            asked.Add(which);
            return null;
        };
        var settings = new SettingsViewModel(app.Services);

        settings.SetShortcut(GlobalShortcut.Ruler, "Ctrl+Alt+P");

        Assert.Equal("Ctrl+Alt+P", app.Services.Settings.Screenshots.RulerShortcut);
        Assert.Equal("Ctrl+Alt+P", settings.RulerShortcut);
        Assert.Equal([GlobalShortcut.Ruler], asked);
        Assert.False(settings.ShortcutProblem);
        Assert.Contains("Ctrl+Alt+P", settings.ShortcutMessage, StringComparison.Ordinal);
        // The others are where they were.
        Assert.Equal(ScreenshotSettings.DefaultShortcut, app.Services.Settings.Screenshots.Shortcut);
        settings.Dispose();
    });

    [Fact]
    public Task Keys_another_shortcut_has_are_refused_and_nothing_changes() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var settings = new SettingsViewModel(app.Services);

        settings.SetShortcut(GlobalShortcut.Ruler, ScreenshotSettings.DefaultShortcut);

        Assert.True(settings.ShortcutProblem);
        Assert.Equal("That's the screenshot shortcut already. Pick other keys for the screen ruler.", settings.ShortcutMessage);
        Assert.Equal(ScreenshotSettings.DefaultRulerShortcut, app.Services.Settings.Screenshots.RulerShortcut);

        // The quick note's keys are kept with the notes, and still can't be taken.
        settings.SetShortcut(GlobalShortcut.Screenshot, Webignite.Core.Notes.NoteSettings.DefaultShortcut);
        Assert.True(settings.ShortcutProblem);
        Assert.Equal(ScreenshotSettings.DefaultShortcut, app.Services.Settings.Screenshots.Shortcut);
        settings.Dispose();
    });

    [Fact]
    public Task Keys_that_are_no_shortcut_are_refused_with_why() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var settings = new SettingsViewModel(app.Services);

        settings.SetShortcut(GlobalShortcut.Snippets, "S");

        Assert.True(settings.ShortcutProblem);
        Assert.NotEmpty(settings.ShortcutMessage);
        Assert.Equal(ScreenshotSettings.DefaultSnippetShortcut, app.Services.Settings.Screenshots.SnippetShortcut);
        settings.Dispose();
    });

    [Fact]
    public Task Keys_another_program_holds_are_kept_but_said_to_be_taken() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Services.ApplyShortcut = _ => "Another program already uses Ctrl+Alt+P. Pick other keys in Settings, Screenshots.";
        var settings = new SettingsViewModel(app.Services);

        settings.SetShortcut(GlobalShortcut.ScreenText, "Ctrl+Alt+P");

        Assert.True(settings.ShortcutProblem);
        Assert.StartsWith("Another program already uses", settings.ShortcutMessage, StringComparison.Ordinal);
        settings.Dispose();
    });

    [Fact]
    public Task Every_shortcut_can_be_turned_off_and_put_back() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var settings = new SettingsViewModel(app.Services);

        foreach (var info in GlobalShortcuts.All)
        {
            settings.ShortcutOffCommand.Execute(info.Shortcut);
            Assert.Equal("", Saved(app, info.Shortcut));
            Assert.Equal("Off", Shown(settings, info.Shortcut));

            settings.ShortcutBackCommand.Execute(info.Shortcut);
            Assert.Equal(info.DefaultKeys, Saved(app, info.Shortcut));
            Assert.Equal(info.DefaultKeys, Shown(settings, info.Shortcut));
        }
        settings.Dispose();
    });

    [Fact]
    public Task The_quick_notes_shortcut_speaks_on_its_own_line_in_Notes() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var settings = new SettingsViewModel(app.Services);

        settings.SetShortcut(GlobalShortcut.QuickNote, "Ctrl+Alt+J");

        Assert.Equal("Ctrl+Alt+J", app.Services.Settings.Notes.QuickShortcut);
        Assert.Equal("Ctrl+Alt+J", settings.QuickNoteShortcut);
        Assert.Contains("Ctrl+Alt+J", settings.QuickNoteMessage, StringComparison.Ordinal);
        Assert.Equal("", settings.ShortcutMessage);
        settings.Dispose();
    });

    private static string Saved(TestApp app, GlobalShortcut which) =>
        GlobalShortcuts.Keys(which, app.Services.Settings.Screenshots, app.Services.Settings.Notes.QuickShortcut);

    private static string Shown(SettingsViewModel settings, GlobalShortcut which) => which switch
    {
        GlobalShortcut.Screenshot => settings.ScreenshotShortcut,
        GlobalShortcut.Recording => settings.RecordShortcut,
        GlobalShortcut.QuickNote => settings.QuickNoteShortcut,
        GlobalShortcut.ColourPicker => settings.ColourShortcut,
        GlobalShortcut.ClipboardHistory => settings.ClipboardShortcut,
        GlobalShortcut.Ruler => settings.RulerShortcut,
        GlobalShortcut.Snippets => settings.SnippetShortcut,
        GlobalShortcut.ScreenText => settings.TextShortcut,
        _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
    };
}
