using System.Runtime.InteropServices;
using Webignite.Tools.Recording;

namespace Webignite.Tests;

/// <summary>
/// The MP4 writer, reader and trimmer, through Windows' own Media Foundation. They run on the Windows build;
/// elsewhere there is no Media Foundation, so they are skipped.
/// </summary>
public sealed class VideoTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-video-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A video player or virus checker may still hold a file for a moment.
        }
    }

    [Fact]
    public void A_recording_reads_back_the_right_way_up_and_trims()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Media Foundation is part of Windows.");
            return;
        }
        const int width = 320, height = 240, fps = 20;
        var path = Path.Combine(folder, "recording.mp4");
        // Top half red, bottom half blue: a picture that shows at once if it comes back upside down.
        var frame = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                frame[at] = (byte)(y < height / 2 ? 0 : 255);   // blue
                frame[at + 2] = (byte)(y < height / 2 ? 255 : 0); // red
                frame[at + 3] = 255;
            }
        var tone = new byte[VideoWriter.AudioRate / 10 * VideoWriter.AudioBytesPerFrame];
        for (var i = 0; i < tone.Length / 4; i++)
        {
            var value = (short)(Math.Sin(i * 2 * Math.PI * 440 / VideoWriter.AudioRate) * 8000);
            BitConverter.TryWriteBytes(tone.AsSpan(i * 4), value);
            BitConverter.TryWriteBytes(tone.AsSpan(i * 4 + 2), value);
        }
        using (var writer = new VideoWriter(path, width, height, fps, withSound: true))
        {
            var pinned = GCHandle.Alloc(frame, GCHandleType.Pinned);
            try
            {
                for (var i = 0; i < 2 * fps; i++)
                    writer.WriteFrame(pinned.AddrOfPinnedObject(), width * 4, i * 10_000_000L / fps, 10_000_000L / fps);
            }
            finally
            {
                pinned.Free();
            }
            for (var i = 0; i < 20; i++)
                writer.WriteSound(tone, tone.Length, i * 1_000_000L);
            writer.Finish();
        }

        var length = VideoFile.Length(path);
        Assert.InRange(length.TotalSeconds, 1.8, 2.2);
        var back = VideoFile.FrameAt(path, TimeSpan.FromSeconds(1))!;
        Assert.Equal((width, height), (back.Width, back.Height));
        var top = (height / 4 * width + width / 2) * 4;
        var bottom = (height * 3 / 4 * width + width / 2) * 4;
        Assert.True(back.Pixels[top + 2] > 180 && back.Pixels[top] < 80, "The top should still be red");
        Assert.True(back.Pixels[bottom] > 180 && back.Pixels[bottom + 2] < 80, "The bottom should still be blue");

        var trimmed = Path.Combine(folder, "trimmed.mp4");
        VideoFile.Trim(path, trimmed, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5), fps);
        Assert.InRange(VideoFile.Length(trimmed).TotalSeconds, 0.8, 1.2);
        Assert.NotNull(VideoFile.FrameAt(trimmed, TimeSpan.FromSeconds(0.5)));
    }
}
