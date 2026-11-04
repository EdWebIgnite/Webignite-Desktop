using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class RecordingTests
{
    [Fact]
    public void Video_areas_are_even_and_big_enough()
    {
        Assert.Equal(new ScreenGrab.Area(10, 20, 640, 480), Recordings.VideoArea(new(10, 20, 641, 481)));
        Assert.Null(Recordings.VideoArea(new(0, 0, 20, 400)));
    }

    [Fact]
    public void Bitrate_stays_between_sensible_limits()
    {
        Assert.Equal(1_500_000, Recordings.Bitrate(320, 240, 20));
        Assert.Equal(12_000_000, Recordings.Bitrate(3840, 2160, 30));
        Assert.Equal(6_912_000, Recordings.Bitrate(1920, 1080, 20));
    }

    [Fact]
    public void Trimming_keeps_at_least_a_second_inside_the_recording()
    {
        var length = TimeSpan.FromSeconds(30);
        Assert.Equal((TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(12)), Recordings.Trim(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(5), length));
        Assert.Equal((TimeSpan.FromSeconds(29), length), Recordings.Trim(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(50), length));
        Assert.Equal((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11)), Recordings.Trim(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.2), length));
        Assert.False(Recordings.IsTrimmed(TimeSpan.Zero, length, length));
        Assert.True(Recordings.IsTrimmed(TimeSpan.FromSeconds(1), length, length));
    }

    [Fact]
    public void Times_and_names_read_easily()
    {
        Assert.Equal("0:07", Recordings.Clock(TimeSpan.FromSeconds(7.6)));
        Assert.Equal("12:30", Recordings.Clock(TimeSpan.FromSeconds(750)));
        Assert.Equal("1:02:03", Recordings.Clock(new TimeSpan(1, 2, 3)));
        Assert.Equal("bosveld-builders recording 2026-11-04 2030.mp4", Recordings.FileName("Bosveld Builders", new DateTime(2026, 11, 4, 20, 30, 0)));
    }
}
