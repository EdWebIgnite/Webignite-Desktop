using Webignite.Core.Studio;

namespace Webignite.Tests;

public sealed class PlanetTests
{
    private const uint Main = 0x4466CC, Light = 0xF0F0F0, Dark = 0x202028, Land = 0x55AA55;

    private static int Brightness(uint colour) => (int)((colour >> 16 & 255) + (colour >> 8 & 255) + (colour & 255));

    [Theory]
    [InlineData(PlanetKind.Gas)]
    [InlineData(PlanetKind.Ocean)]
    [InlineData(PlanetKind.Rock)]
    [InlineData(PlanetKind.Ice)]
    [InlineData(PlanetKind.Desert)]
    [InlineData(PlanetKind.Mars)]
    public void A_worlds_map_is_the_same_every_time_has_detail_and_joins_up_left_to_right(PlanetKind kind)
    {
        var map = PlanetSurface.Make(kind, 1234, 64, 32, Main, Light, Dark, Land);

        Assert.Equal(64 * 32, map.Length);
        Assert.Equal(map, PlanetSurface.Make(kind, 1234, 64, 32, Main, Light, Dark, Land));
        Assert.NotEqual(map, PlanetSurface.Make(kind, 99, 64, 32, Main, Light, Dark, Land));
        Assert.True(map.Distinct().Count() > 20);
        // No seam: the last column is no more unlike the first than any two neighbours are.
        double Jump(int from, int to) => Enumerable.Range(0, 32).Average(row => Math.Abs(Brightness(map[row * 64 + from]) - Brightness(map[row * 64 + to])));
        var usual = Enumerable.Range(0, 63).Average(column => Jump(column, column + 1));
        Assert.True(Jump(63, 0) <= usual * 2.5 + 6, $"{kind}: the join jumps {Jump(63, 0):0.0} against a usual {usual:0.0}");
    }

    [Fact]
    public void Each_client_gets_one_of_six_kinds_of_world_always_the_same()
    {
        Assert.Equal(PlanetSurface.KindOf(42), PlanetSurface.KindOf(42));
        Assert.Equal(6, Enumerable.Range(0, 50).Select(PlanetSurface.KindOf).Distinct().Count());
    }

    [Fact]
    public void Mars_is_rust_with_bright_caps_at_its_poles()
    {
        const uint rust = 0xB5553A;
        var map = PlanetSurface.Make(PlanetKind.Mars, 5, 128, 64, rust, Light, Dark, Land);

        double Red(uint c) => c >> 16 & 255;
        double Blue(uint c) => c & 255;
        var top = Enumerable.Range(0, 128).Average(x => (double)Brightness(map[x]));
        var bottom = Enumerable.Range(0, 128).Average(x => (double)Brightness(map[63 * 128 + x]));
        var middle = Enumerable.Range(0, 128).Select(x => map[32 * 128 + x]).ToList();
        // The poles are far brighter than the plains, and the plains are redder than they are blue.
        Assert.True(top > middle.Average(c => (double)Brightness(c)) + 150);
        Assert.True(bottom > middle.Average(c => (double)Brightness(c)) + 150);
        Assert.True(middle.Average(Red) > middle.Average(Blue) + 50);
    }

    [Fact]
    public void A_globe_is_round_lit_from_the_upper_left_and_dark_on_the_far_side()
    {
        var sphere = new PlanetSphere(64, 0, 64, 32);
        var map = Enumerable.Repeat(0xC0C0C0u, 64 * 32).ToArray();
        var picture = new int[64 * 64];

        sphere.Render(map, 0, 0x101014, 0x8090FF, picture);

        int At(int x, int y) => picture[y * 64 + x];
        // Clear in the corners, solid in the middle.
        Assert.Equal(0, At(0, 0));
        Assert.Equal(0, At(63, 63));
        Assert.Equal(255, At(32, 32) >>> 24);
        // The upper left is in daylight, the lower right in night.
        Assert.True(Brightness((uint)At(20, 20)) > Brightness((uint)At(46, 46)) * 4);
        Assert.True(Brightness((uint)At(46, 46)) < 120);
    }

    [Fact]
    public void A_globe_turns_and_comes_round_to_where_it_started()
    {
        var sphere = new PlanetSphere(48, 0.2, 64, 32);
        var map = PlanetSurface.Make(PlanetKind.Ocean, 7, 64, 32, Main, Light, Dark, Land);
        int[] Drawn(double turned)
        {
            var picture = new int[48 * 48];
            sphere.Render(map, turned, 0x101014, 0x8090FF, picture);
            return picture;
        }

        Assert.NotEqual(Drawn(0), Drawn(8));
        Assert.Equal(Drawn(0), Drawn(64));
        // A part of a dot's turn changes the picture too, so the turning is smooth.
        Assert.NotEqual(Drawn(0), Drawn(0.5));
    }

    [Fact]
    public void A_map_width_that_isnt_a_power_of_two_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetSphere(32, 0, 100, 50));
}
