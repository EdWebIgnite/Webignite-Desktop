using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Webignite.Core.Tools;

namespace Webignite.Tools.Recording;

/// <summary>A video frame: top row first, four bytes a pixel (blue, green, red, unused).</summary>
public sealed record VideoFrame(byte[] Pixels, int Width, int Height);

/// <summary>
/// Writes an MP4: H.264 video from screen frames, and AAC sound when there is any. Frames and sound can come in
/// from two threads; writing is one at a time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VideoWriter : IDisposable
{
    public const int AudioRate = 48000;
    public const int AudioChannels = 2;
    public const int AudioBytesPerFrame = AudioChannels * 2;

    private readonly IDisposable mediaFoundation;
    private readonly IMFSinkWriter writer;
    private readonly int video;
    private readonly int audio = -1;
    private readonly int width;
    private readonly int height;
    private readonly Lock gate = new();
    private bool finished;

    public VideoWriter(string path, int width, int height, int framesPerSecond, bool withSound)
    {
        this.width = width;
        this.height = height;
        mediaFoundation = MF.Use();
        try
        {
            MF.Check(MF.MFCreateAttributes(out var settings, 1));
            settings.SetUINT32(MF.EnableHardwareTransforms, 1);
            MF.Check(MF.MFCreateSinkWriterFromURL(path, IntPtr.Zero, settings, out writer));
            Marshal.ReleaseComObject(settings);

            var output = Type(MF.Video, MF.H264);
            output.SetUINT32(MF.AvgBitrate, Recordings.Bitrate(width, height, framesPerSecond));
            Picture(output, framesPerSecond);
            MF.Check(writer.AddStream(output, out video));
            var input = Type(MF.Video, MF.Rgb32);
            Picture(input, framesPerSecond);
            // Positive: the top row comes first, as the screen gives it.
            input.SetUINT32(MF.DefaultStride, width * 4);
            MF.Check(writer.SetInputMediaType(video, input, null));
            Marshal.ReleaseComObject(output);
            Marshal.ReleaseComObject(input);

            if (withSound)
            {
                var aac = Type(MF.Audio, MF.Aac);
                Sound(aac);
                aac.SetUINT32(MF.AudioAvgBytesPerSecond, 24000); // 192 kbit/s
                MF.Check(writer.AddStream(aac, out audio));
                var pcm = Type(MF.Audio, MF.Pcm);
                Sound(pcm);
                pcm.SetUINT32(MF.AudioBlockAlignment, AudioBytesPerFrame);
                pcm.SetUINT32(MF.AudioAvgBytesPerSecond, AudioRate * AudioBytesPerFrame);
                MF.Check(writer.SetInputMediaType(audio, pcm, null));
                Marshal.ReleaseComObject(aac);
                Marshal.ReleaseComObject(pcm);
            }
            MF.Check(writer.BeginWriting());
        }
        catch
        {
            mediaFoundation.Dispose();
            throw;
        }
    }

    public bool HasSound => audio >= 0;

    private static IMFMediaType Type(Guid major, Guid subtype)
    {
        MF.Check(MF.MFCreateMediaType(out var type));
        type.SetGUID(MF.MajorType, major);
        type.SetGUID(MF.Subtype, subtype);
        return type;
    }

    private void Picture(IMFMediaType type, int framesPerSecond)
    {
        type.SetUINT32(MF.InterlaceMode, 2); // progressive
        type.SetUINT64(MF.FrameSize, MF.Pack(width, height));
        type.SetUINT64(MF.FrameRate, MF.Pack(framesPerSecond, 1));
        type.SetUINT64(MF.PixelAspectRatio, MF.Pack(1, 1));
    }

    private static void Sound(IMFMediaType type)
    {
        type.SetUINT32(MF.AudioBitsPerSample, 16);
        type.SetUINT32(MF.AudioSamplesPerSecond, AudioRate);
        type.SetUINT32(MF.AudioChannels, AudioChannels);
    }

    /// <summary>One frame, at a time in 100-nanosecond units from the start. The source may run top row first (a positive stride) or bottom row first.</summary>
    public void WriteFrame(IntPtr pixels, int stride, long time, long duration)
    {
        var length = width * height * 4;
        MF.Check(MF.MFCreateMemoryBuffer(length, out var buffer));
        MF.Check(buffer.Lock(out var target, out _, out _));
        try
        {
            Frames.Copy(target, pixels, width, height, stride);
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.SetCurrentLength(length);
        Write(video, buffer, time, duration);
    }

    /// <summary>Sound: 16-bit stereo at 48 kHz, at a time in 100-nanosecond units from the start.</summary>
    public void WriteSound(byte[] pcm, int length, long time)
    {
        if (audio < 0 || length <= 0)
            return;
        MF.Check(MF.MFCreateMemoryBuffer(length, out var buffer));
        MF.Check(buffer.Lock(out var target, out _, out _));
        try
        {
            Marshal.Copy(pcm, 0, target, length);
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.SetCurrentLength(length);
        Write(audio, buffer, time, length / AudioBytesPerFrame * 10_000_000L / AudioRate);
    }

    private void Write(int stream, IMFMediaBuffer buffer, long time, long duration)
    {
        MF.Check(MF.MFCreateSample(out var sample));
        try
        {
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            lock (gate)
            {
                if (!finished)
                    MF.Check(writer.WriteSample(stream, sample));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buffer);
        }
    }

    /// <summary>Writes what is left and closes the file. Nothing more can be written after.</summary>
    public void Finish()
    {
        lock (gate)
        {
            if (finished)
                return;
            finished = true;
            MF.Check(writer.Finalize_());
        }
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(writer);
        mediaFoundation.Dispose();
    }
}

/// <summary>Reading an MP4 back: how long it is, a frame at a time, and a trimmed copy.</summary>
[SupportedOSPlatform("windows")]
public static class VideoFile
{
    public static TimeSpan Length(string path)
    {
        using var started = MF.Use();
        var reader = Open(path);
        try
        {
            MF.Check(reader.GetPresentationAttribute(MF.MediaSource, MF.Duration, out var duration));
            return TimeSpan.FromTicks(duration.Value);
        }
        finally
        {
            Marshal.ReleaseComObject(reader);
        }
    }

    /// <summary>The frame showing at a time, or null past the end.</summary>
    public static VideoFrame? FrameAt(string path, TimeSpan at)
    {
        using var started = MF.Use();
        var reader = Open(path);
        try
        {
            var (width, height, stride) = PictureOut(reader);
            reader.SetStreamSelection(MF.AllStreams, 0);
            reader.SetStreamSelection(MF.FirstVideoStream, 1);
            MF.Check(reader.SetCurrentPosition(Guid.Empty, PropVariant.Long(at.Ticks)));
            VideoFrame? last = null;
            while (true)
            {
                MF.Check(reader.ReadSample(MF.FirstVideoStream, 0, out _, out var flags, out var time, out var sample));
                if (sample is null)
                    return last;
                try
                {
                    last = Copy(sample, width, height, stride);
                }
                finally
                {
                    Marshal.ReleaseComObject(sample);
                }
                // After a seek the reader starts at the key frame before; read on to the time asked for.
                if (time >= at.Ticks - 500_000 || (flags & MF.EndOfStream) != 0)
                    return last;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(reader);
        }
    }

    /// <summary>A copy of the part from start to end, the sound with it. The recording itself isn't changed.</summary>
    public static void Trim(string path, string output, TimeSpan start, TimeSpan end, int framesPerSecond)
    {
        using var started = MF.Use();
        var reader = Open(path);
        try
        {
            var (width, height, stride) = PictureOut(reader);
            var videoIndex = StreamIndex(reader, MF.Video);
            var audioIndex = StreamIndex(reader, MF.Audio);
            if (audioIndex >= 0)
            {
                var pcm = Type(MF.Audio, MF.Pcm);
                pcm.SetUINT32(MF.AudioBitsPerSample, 16);
                pcm.SetUINT32(MF.AudioSamplesPerSecond, VideoWriter.AudioRate);
                pcm.SetUINT32(MF.AudioChannels, VideoWriter.AudioChannels);
                MF.Check(reader.SetCurrentMediaType(audioIndex, IntPtr.Zero, pcm));
                Marshal.ReleaseComObject(pcm);
            }
            MF.Check(reader.SetCurrentPosition(Guid.Empty, PropVariant.Long(start.Ticks)));
            using var writer = new VideoWriter(output, width, height, framesPerSecond, audioIndex >= 0);
            var frameTime = 10_000_000L / framesPerSecond;
            var soundBuffer = Array.Empty<byte>();
            var videoDone = false;
            var audioDone = audioIndex < 0;
            while (!videoDone || !audioDone)
            {
                MF.Check(reader.ReadSample(MF.AllStreams, 0, out var index, out var flags, out var time, out var sample));
                var isVideo = index == videoIndex;
                if (sample is not null)
                {
                    try
                    {
                        if (time >= start.Ticks && time < end.Ticks)
                        {
                            if (isVideo)
                                WriteVideo(writer, sample, time - start.Ticks, frameTime, stride);
                            else if (index == audioIndex)
                                soundBuffer = WriteSound(writer, sample, time - start.Ticks, soundBuffer);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                }
                if ((flags & MF.EndOfStream) != 0 || time >= end.Ticks)
                {
                    if (isVideo)
                        videoDone = true;
                    else if (index == audioIndex)
                        audioDone = true;
                    reader.SetStreamSelection(index, 0);
                }
            }
            writer.Finish();
        }
        finally
        {
            Marshal.ReleaseComObject(reader);
        }
    }

    private static void WriteVideo(VideoWriter writer, IMFSample sample, long time, long duration, int stride)
    {
        MF.Check(sample.ConvertToContiguousBuffer(out var buffer));
        MF.Check(buffer.Lock(out var pixels, out _, out _));
        try
        {
            writer.WriteFrame(pixels, stride, time, duration);
        }
        finally
        {
            buffer.Unlock();
            Marshal.ReleaseComObject(buffer);
        }
    }

    private static byte[] WriteSound(VideoWriter writer, IMFSample sample, long time, byte[] reuse)
    {
        MF.Check(sample.ConvertToContiguousBuffer(out var buffer));
        MF.Check(buffer.Lock(out var data, out _, out var length));
        try
        {
            if (reuse.Length < length)
                reuse = new byte[length];
            Marshal.Copy(data, reuse, 0, length);
            writer.WriteSound(reuse, length, time);
            return reuse;
        }
        finally
        {
            buffer.Unlock();
            Marshal.ReleaseComObject(buffer);
        }
    }

    private static IMFSourceReader Open(string path)
    {
        MF.Check(MF.MFCreateAttributes(out var settings, 1));
        settings.SetUINT32(MF.EnableVideoProcessing, 1);
        try
        {
            MF.Check(MF.MFCreateSourceReaderFromURL(path, settings, out var reader));
            return reader;
        }
        finally
        {
            Marshal.ReleaseComObject(settings);
        }
    }

    private static IMFMediaType Type(Guid major, Guid subtype)
    {
        MF.Check(MF.MFCreateMediaType(out var type));
        type.SetGUID(MF.MajorType, major);
        type.SetGUID(MF.Subtype, subtype);
        return type;
    }

    // Asks for the picture as RGB32 and reads its size and which way up it comes.
    private static (int Width, int Height, int Stride) PictureOut(IMFSourceReader reader)
    {
        var rgb = Type(MF.Video, MF.Rgb32);
        MF.Check(reader.SetCurrentMediaType(MF.FirstVideoStream, IntPtr.Zero, rgb));
        Marshal.ReleaseComObject(rgb);
        MF.Check(reader.GetCurrentMediaType(MF.FirstVideoStream, out var current));
        try
        {
            MF.Check(current.GetUINT64(MF.FrameSize, out var size));
            var width = (int)(size >> 32);
            var height = (int)(size & 0xFFFFFFFF);
            var stride = current.GetUINT32(MF.DefaultStride, out var given) >= 0 ? given : width * 4;
            return (width, height, stride);
        }
        finally
        {
            Marshal.ReleaseComObject(current);
        }
    }

    private static int StreamIndex(IMFSourceReader reader, Guid major)
    {
        for (var i = 0; i < 8; i++)
        {
            if (reader.GetCurrentMediaType(i, out var type) < 0)
                break;
            try
            {
                if (type.GetMajorType(out var found) >= 0 && found == major)
                    return i;
            }
            finally
            {
                Marshal.ReleaseComObject(type);
            }
        }
        return -1;
    }

    private static VideoFrame Copy(IMFSample sample, int width, int height, int stride)
    {
        MF.Check(sample.ConvertToContiguousBuffer(out var buffer));
        MF.Check(buffer.Lock(out var data, out _, out _));
        try
        {
            var pixels = new byte[width * height * 4];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                Frames.Copy(handle.AddrOfPinnedObject(), data, width, height, stride);
            }
            finally
            {
                handle.Free();
            }
            return new VideoFrame(pixels, width, height);
        }
        finally
        {
            buffer.Unlock();
            Marshal.ReleaseComObject(buffer);
        }
    }
}

/// <summary>Copying a picture into a top-row-first buffer, from a source that runs either way up.</summary>
internal static class Frames
{
    public static void Copy(IntPtr target, IntPtr source, int width, int height, int stride)
    {
        var row = width * 4;
        if (stride == row)
        {
            CopyMemory(target, source, (nuint)(row * height));
            return;
        }
        // A negative stride: the buffer holds the bottom row first.
        var step = Math.Abs(stride);
        for (var y = 0; y < height; y++)
        {
            var from = stride > 0 ? y : height - 1 - y;
            CopyMemory(target + y * row, source + from * step, (nuint)row);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr target, IntPtr source, nuint length);
}
