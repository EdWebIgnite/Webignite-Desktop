using Webignite.Core.Layout;
using Webignite.Core.Tools;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>The list of keyboard shortcuts, as F1 and Settings show it.</summary>
public sealed class ShortcutListTests
{
    [Fact]
    public Task F1_lists_the_shortcuts_under_headings_with_the_open_pages_first() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);
        main.ToggleShortcutsCommand.Execute(null);
        Assert.Equal(ShortcutList.Everywhere, main.ShortcutsPart.Groups[0].Name);
        main.ToggleShortcutsCommand.Execute(null);
        // Closed, it holds nothing.
        Assert.Empty(main.ShortcutsPart.Groups);

        main.Selected = main[Page.Mail];
        main.ToggleShortcutsCommand.Execute(null);

        Assert.Equal(ShortcutList.Mail, main.ShortcutsPart.Groups[0].Name);
        Assert.Contains(main.ShortcutsPart.Groups, g => g.Name == ShortcutList.Files);
    });

    [Fact]
    public Task Typing_narrows_the_list_and_says_when_nothing_is_found() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);
        main.ToggleShortcutsCommand.Execute(null);

        main.ShortcutsPart.Search = "recycle";
        Assert.Equal("F2, Delete", Assert.Single(Assert.Single(main.ShortcutsPart.Groups).Items).Keys);

        main.ShortcutsPart.Search = "zebra";
        Assert.Empty(main.ShortcutsPart.Groups);
        Assert.Contains("zebra", main.ShortcutsPart.Nothing, StringComparison.Ordinal);

        // Opened again, the search box is empty and everything is back.
        main.ToggleShortcutsCommand.Execute(null);
        main.ToggleShortcutsCommand.Execute(null);
        Assert.Equal("", main.ShortcutsPart.Search);
        Assert.True(main.ShortcutsPart.Groups.Count > 5);
    });

    [Fact]
    public void The_ones_from_any_program_show_the_keys_picked_in_Settings_and_not_those_turned_off()
    {
        var shots = new ScreenshotSettings { Shortcut = "Ctrl+Shift+F9", RulerShortcut = "" };
        var any = MainViewModel.AllShortcuts(shots, "Ctrl+Alt+N").Where(s => s.Group == ShortcutList.AnyProgram).ToList();

        Assert.Contains(any, s => s.Keys == "Ctrl+Shift+F9" && s.What.StartsWith("Take a screenshot", StringComparison.Ordinal));
        Assert.DoesNotContain(any, s => s.What.StartsWith("Measure", StringComparison.Ordinal));
        Assert.Equal(GlobalShortcuts.All.Count - 1, any.Count);
    }

    [Fact]
    public Task Settings_has_the_list_in_its_own_section_filled_only_while_open() => Ui.Run(() =>
    {
        using var app = new TestApp();
        using var settings = new SettingsViewModel(app.Services);
        Assert.Empty(settings.Shortcuts.Groups);

        settings.Open("shortcuts");
        Assert.True(settings.ShowShortcuts);
        Assert.Equal(ShortcutList.Everywhere, settings.Shortcuts.Groups[0].Name);

        settings.OpenSectionCommand.Execute("screenshots");
        Assert.True(settings.ShowScreenshots);
        Assert.Empty(settings.Shortcuts.Groups);
    });
}
