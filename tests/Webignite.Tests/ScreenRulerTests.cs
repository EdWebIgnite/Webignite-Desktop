using Webignite.Core.Tools;

namespace Webignite.Tests;

public class ScreenRulerTests
{
    private const uint White = 0xFFFFFFFF;
    private const uint Black = 0xFF000000;
    private const uint Orange = 0xFFE8590C;

    [Fact]
    public void Pointing_at_a_gap_measures_from_one_edge_to_the_other()
    {
        uint[] line = [Black, Black, White, White, White, White, Orange, Orange];
        Assert.Equal(new PixelRun(2, 5), ScreenRuler.Reach(line, 3));
        Assert.Equal(4, ScreenRuler.Reach(line, 3).Length);
        Assert.Equal(new PixelRun(0, 1), ScreenRuler.Reach(line, 0));
        Assert.Equal(new PixelRun(6, 7), ScreenRuler.Reach(line, 7));
    }

    [Fact]
    public void A_stretch_runs_to_the_edge_of_the_screen_when_nothing_stops_it()
    {
        uint[] line = [White, White, White];
        Assert.Equal(new PixelRun(0, 2), ScreenRuler.Reach(line, 1));
        Assert.Equal(new PixelRun(0, 2), ScreenRuler.Reach(line, 99));
        Assert.Equal(new PixelRun(0, 0), ScreenRuler.Reach([], 0));
    }

    [Fact]
    public void A_slightly_different_shade_is_the_same_stretch_and_a_clearly_different_one_ends_it()
    {
        // The smoothing at the edge of a letter drifts a little; a border doesn't.
        uint[] line = [0xFFFFFFFF, 0xFFFAFAFA, 0xFFF8F8F8, 0xFFE0E0E0, 0xFFFFFFFF];
        Assert.Equal(new PixelRun(0, 2), ScreenRuler.Reach(line, 0));
        // The see-through byte at the top is ignored.
        Assert.Equal(new PixelRun(0, 1), ScreenRuler.Reach([0xFFFFFFFF, 0x00FFFFFF, 0xFF000000], 0));
    }

    [Fact]
    public void A_box_is_the_same_whichever_way_it_was_dragged()
    {
        Assert.Equal((100, 100, new Measured(201, 121)), ScreenRuler.Box(100, 100, 300, 220));
        Assert.Equal((100, 100, new Measured(201, 121)), ScreenRuler.Box(300, 220, 100, 100));
        Assert.Equal((5, 5, new Measured(1, 1)), ScreenRuler.Box(5, 5, 5, 5));
    }

    [Fact]
    public void The_size_is_said_in_pixels_and_at_100_percent_on_an_enlarged_screen()
    {
        Assert.Equal("480 × 270", ScreenRuler.Plain(new Measured(480, 270)));
        Assert.Equal("480 × 270 px", ScreenRuler.Words(new Measured(480, 270), 1));
        Assert.Equal("480 × 270 px (320 × 180 at 100%)", ScreenRuler.Words(new Measured(480, 270), 1.5));
        Assert.Equal("480 × 270 px (384 × 216 at 100%)", ScreenRuler.Words(new Measured(480, 270), 1.25));
    }

    [Fact]
    public void The_rulers_shortcut_cant_take_another_shortcuts_keys()
    {
        var shots = new ScreenshotSettings();
        Assert.Equal("Ctrl+Alt+M", shots.RulerShortcut);
        var (colour, _) = Hotkey.Parse(shots.ColourShortcut);
        Assert.Equal(GlobalShortcut.ColourPicker, GlobalShortcuts.TakenBy(colour!, shots, "Ctrl+Alt+N", GlobalShortcut.Ruler));
        var (ruler, _) = Hotkey.Parse(shots.RulerShortcut);
        Assert.Equal(GlobalShortcut.Ruler, GlobalShortcuts.TakenBy(ruler!, shots, "Ctrl+Alt+N", GlobalShortcut.Screenshot));
        Assert.Null(GlobalShortcuts.TakenBy(ruler!, shots, "Ctrl+Alt+N", GlobalShortcut.Ruler));
        Assert.Equal("That's the screen ruler shortcut already. Pick other keys for screenshots.", GlobalShortcuts.Clash(GlobalShortcut.Ruler, GlobalShortcut.Screenshot));
    }
}
