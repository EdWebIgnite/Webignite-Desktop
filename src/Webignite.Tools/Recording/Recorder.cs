using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Webignite.Core.Tools;

namespace Webignite.Tools.Recording;

/// <summary>
/// Records part of the screen to an MP4, with the microphone when asked and there is one. Frames are grabbed
/// on a thread of its own, about 20 a second, with the mouse pointer drawn in; sound comes in on another.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenRecorder
{
    private readonly Stopwatch clock = new();
    private volatile bool stopping;
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ScreenGrab.Area area;
    private readonly string path;
    private readonly bool wantSound;

    private ScreenRecorder(ScreenGrab.Area area, string path, bool microphone)
    {
        this.area = area;
        this.path = path;
        wantSound = microphone;
    }

    /// <summary>Starts recording an area of the screen (even width and height, see <see cref="Recordings.VideoArea"/>).</summary>
    public static ScreenRecorder Start(ScreenGrab.Area area, string path, bool microphone)
    {
        var recorder = new ScreenRecorder(area, path, microphone);
        var thread = new Thread(recorder.Run) { IsBackground = true, Name = "Screen recording" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return recorder;
    }

    public TimeSpan Elapsed => clock.Elapsed;

    /// <summary>Why there's no sound, when the microphone was asked for and couldn't be used.</summary>
    public string? SoundProblem { get; private set; }

    /// <summary>Stops, finishes the file and returns once it can be played.</summary>
    public Task Stop()
    {
        stopping = true;
        return finished.Task;
    }

    private void Run()
    {
        Microphone? microphone = null;
        try
        {
            if (wantSound)
            {
                microphone = Microphone.Open(out var problem);
                SoundProblem = problem;
            }
            using var writer = new VideoWriter(path, area.Width, area.Height, Recordings.FramesPerSecond, microphone is not null);
            using var frames = new ScreenFrames(area);
            clock.Start();
            long soundFrames = 0;
            microphone?.Start((data, length) =>
            {
                writer.WriteSound(data, length, soundFrames * 10_000_000L / VideoWriter.AudioRate);
                soundFrames += length / VideoWriter.AudioBytesPerFrame;
            });
            var interval = TimeSpan.FromSeconds(1.0 / Recordings.FramesPerSecond);
            var next = TimeSpan.Zero;
            while (!stopping)
            {
                var now = clock.Elapsed;
                if (now < next)
                {
                    Thread.Sleep(next - now);
                    continue;
                }
                frames.Grab();
                writer.WriteFrame(frames.Pixels, area.Width * 4, now.Ticks, interval.Ticks);
                // A slow frame skips the ones it ran over, rather than piling up.
                next += interval;
                if (next < clock.Elapsed)
                    next = clock.Elapsed + interval;
            }
            microphone?.Stop();
            writer.Finish();
            finished.TrySetResult();
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or DllNotFoundException)
        {
            finished.TrySetException(new IOException("The recording stopped: " + e.Message, e));
        }
        finally
        {
            microphone?.Dispose();
            clock.Stop();
        }
    }
}

/// <summary>Grabs an area of the screen into one reused picture, with the mouse pointer drawn on.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ScreenFrames : IDisposable
{
    private readonly ScreenGrab.Area area;
    private readonly IntPtr screen;
    private readonly IntPtr memory;
    private readonly IntPtr bitmap;
    private readonly IntPtr old;

    public ScreenFrames(ScreenGrab.Area area)
    {
        this.area = area;
        screen = GetDC(IntPtr.Zero);
        memory = CreateCompatibleDC(screen);
        var info = new BitmapInfo { Size = 40, Width = area.Width, Height = -area.Height, Planes = 1, BitCount = 32 };
        bitmap = CreateDIBSection(memory, ref info, 0, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero)
            throw new IOException("Windows wouldn't give room for the recording's pictures.");
        Pixels = bits;
        old = SelectObject(memory, bitmap);
    }

    /// <summary>The latest frame: top row first, four bytes a pixel.</summary>
    public IntPtr Pixels { get; }

    public void Grab()
    {
        BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, 0x00CC0020 | 0x40000000);
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (GetCursorInfo(ref cursor) && (cursor.Flags & 1) != 0 && GetIconInfo(cursor.Cursor, out var icon))
        {
            DrawIconEx(memory, cursor.X - area.X - icon.HotspotX, cursor.Y - area.Y - icon.HotspotY, cursor.Cursor, 0, 0, 0, IntPtr.Zero, 3);
            if (icon.Mask != IntPtr.Zero)
                DeleteObject(icon.Mask);
            if (icon.Colour != IntPtr.Zero)
                DeleteObject(icon.Colour);
        }
        GdiFlush();
    }

    public void Dispose()
    {
        SelectObject(memory, old);
        DeleteObject(bitmap);
        DeleteDC(memory);
        if (ReleaseDC(IntPtr.Zero, screen) == 0)
            Debug.WriteLine("Windows didn't take the screen back.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant, Colours;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size, Flags;
        public IntPtr Cursor;
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX, HotspotY;
        public IntPtr Mask, Colour;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr thing);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr thing);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
}

/// <summary>
/// The default microphone through Windows' audio system (WASAPI), asked for 16-bit stereo at 48 kHz, which
/// Windows converts to from whatever the microphone gives.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Microphone : IDisposable
{
    private readonly IAudioClient client;
    private readonly IAudioCaptureClient capture;
    private readonly ManualResetEventSlim stop = new();
    private Thread? thread;

    private Microphone(IAudioClient client, IAudioCaptureClient capture)
    {
        this.client = client;
        this.capture = capture;
    }

    /// <summary>The default microphone, or null with why when there isn't one or it can't be opened.</summary>
    public static Microphone? Open(out string? problem)
    {
        problem = null;
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            if (enumerator.GetDefaultAudioEndpoint(1 /* capture */, 0 /* console */, out var device) < 0 || device is null)
            {
                problem = "There's no microphone, so the recording has no sound.";
                return null;
            }
            var clientId = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Check(device.Activate(clientId, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var activated));
            var client = (IAudioClient)activated;
            var format = new WaveFormat
            {
                Tag = 1, // PCM
                Channels = VideoWriter.AudioChannels,
                SamplesPerSecond = VideoWriter.AudioRate,
                AvgBytesPerSecond = VideoWriter.AudioRate * VideoWriter.AudioBytesPerFrame,
                BlockAlign = VideoWriter.AudioBytesPerFrame,
                BitsPerSample = 16,
            };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormat>());
            try
            {
                Marshal.StructureToPtr(format, pointer, false);
                // Shared, with Windows converting the microphone's own format to this one.
                Check(client.Initialize(0, unchecked((int)0x88000000), 10_000_000, 0, pointer, IntPtr.Zero));
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
            Check(client.GetService(new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), out var service));
            return new Microphone(client, (IAudioCaptureClient)service);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException)
        {
            problem = "The microphone couldn't be opened, so the recording has no sound. " + e.Message;
            return null;
        }
    }

    /// <summary>Starts listening; each piece of sound goes to the callback as 16-bit stereo bytes.</summary>
    public void Start(Action<byte[], int> heard)
    {
        Check(client.Start());
        thread = new Thread(() =>
        {
            var buffer = new byte[VideoWriter.AudioRate * VideoWriter.AudioBytesPerFrame];
            while (!stop.Wait(10))
            {
                while (capture.GetNextPacketSize(out var frames) >= 0 && frames > 0)
                {
                    if (capture.GetBuffer(out var data, out var count, out var flags, out _, out _) < 0)
                        break;
                    var length = (int)count * VideoWriter.AudioBytesPerFrame;
                    if (buffer.Length < length)
                        buffer = new byte[length];
                    if ((flags & 2) != 0) // silent
                        Array.Clear(buffer, 0, length);
                    else
                        Marshal.Copy(data, buffer, 0, length);
                    capture.ReleaseBuffer(count);
                    heard(buffer, length);
                }
            }
        }) { IsBackground = true, Name = "Recording sound" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public void Stop()
    {
        stop.Set();
        thread?.Join();
        client.Stop();
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(capture);
        Marshal.ReleaseComObject(client);
        stop.Dispose();
    }

    private static void Check(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public short Tag;
        public short Channels;
        public int SamplesPerSecond;
        public int AvgBytesPerSecond;
        public short BlockAlign;
        public short BitsPerSample;
        public short ExtraSize;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(in Guid id, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long normal, out long least);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(in Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out long devicePosition, out long counterPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
