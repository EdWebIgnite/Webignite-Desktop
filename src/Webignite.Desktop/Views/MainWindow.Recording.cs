using Avalonia;
using Avalonia.Controls;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Screen recording: the shortcut (or Mark-up's button) freezes the screen to pick the part to record, a bar
/// shows the time with Stop, and the same shortcut stops it. The recording then opens in Mark-up to trim and
/// save. Windows only.
/// </summary>
public partial class MainWindow
{
    private GlobalHotkey? recordKey;
    private RecordingSession? recording;

    private sealed record RecordingSession(Webignite.Tools.Recording.ScreenRecorder Recorder, RecordingBar Bar, string Path);

    private void WireRecording(MainViewModel main, AppServices services)
    {
        services.Go.RecordAction = hideApp =>
        {
            if (OperatingSystem.IsWindows())
                (recording is null ? StartRecording(services, hideApp) : StopRecording(main, services)).Forget("Recording the screen", tell: true);
        };
        services.ApplyRecordShortcut = () => ApplyRecordShortcut(services);
        Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows())
                return;
            recordKey = new GlobalHotkey(this, 0x5748, () => services.Go.Record(hideApp: false));
            if (ApplyRecordShortcut(services) is { } problem)
                ShowNote("Recording shortcut not set", problem, () => services.Go.Settings("screenshots"));
        };
        Closed += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                recordKey?.Release();
            recordKey = null;
        };
    }

    private string? ApplyRecordShortcut(AppServices services)
    {
        if (!OperatingSystem.IsWindows() || recordKey is null)
            return null;
        var shortcut = services.Settings.Screenshots.RecordShortcut;
        if (string.IsNullOrWhiteSpace(shortcut))
            return recordKey.Set(null);
        var (keys, problem) = Hotkey.Parse(shortcut);
        return problem ?? recordKey.Set(keys);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task StartRecording(AppServices services, bool hideApp)
    {
        if (grabbing)
            return;
        grabbing = true;
        var before = WindowState;
        try
        {
            if (hideApp && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(350);
            }
            var (x, y) = ScreenCapture.Cursor();
            if ((Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary) is not { } screen)
                return;
            var bounds = screen.Bounds;
            ScreenGrab.Area? picked;
            using (var frozen = ScreenCapture.Grab(new ScreenGrab.Area(bounds.X, bounds.Y, bounds.Width, bounds.Height)))
            {
                var windows = ScreenCapture.Windows(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero)
                    .Select(w => w with { X = w.X - bounds.X, Y = w.Y - bounds.Y })
                    .ToList();
                var picker = new ScreenGrabWindow(frozen, bounds, screen.Scaling, windows, recording: true);
                picker.Show();
                picked = await picker.Picked;
            }
            if (picked is not { } chosen)
            {
                if (hideApp)
                    WindowState = before;
                return;
            }
            if (Recordings.VideoArea(chosen with { X = chosen.X + bounds.X, Y = chosen.Y + bounds.Y }) is not { } area)
            {
                ShowNote("Too small to record", "Drag over a bigger part of the screen.", null);
                if (hideApp)
                    WindowState = before;
                return;
            }
            var folder = Path.Combine(Path.GetTempPath(), "Webignite recordings");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".mp4");
            var microphone = services.Settings.Screenshots.Microphone;
            var recorder = Webignite.Tools.Recording.ScreenRecorder.Start(area, path, microphone);
            var bar = new RecordingBar(() => recorder.Elapsed, () => services.Go.Record(), microphone);
            bar.ShowBeside(new PixelRect(area.X, area.Y, area.Width, area.Height), bounds, screen.Scaling);
            recording = new RecordingSession(recorder, bar, path);
            services.Recording = true;
        }
        finally
        {
            grabbing = false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task StopRecording(MainViewModel main, AppServices services)
    {
        if (recording is not { } session)
            return;
        recording = null;
        services.Recording = false;
        session.Bar.Close();
        try
        {
            await session.Recorder.Stop();
        }
        finally
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
        var video = new WindowsVideo();
        var length = await Task.Run(() => video.Length(session.Path));
        main.Selected = main[Page.Markup];
        main.Markup.ShowRecording(session.Path, length, video, session.Recorder.SoundProblem);
    }

    /// <summary>Closing the app while recording: the recording is finished first, so the file isn't left broken.</summary>
    private async Task FinishRecordingBeforeClose()
    {
        if (!OperatingSystem.IsWindows() || recording is not { } session)
            return;
        recording = null;
        session.Bar.Close();
        try
        {
            await session.Recorder.Stop();
        }
        catch (IOException e)
        {
            Webignite.Data.AppLog.Write("Finishing a recording before closing", e);
        }
    }
}
