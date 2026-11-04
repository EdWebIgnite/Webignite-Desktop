using System.Runtime.Versioning;
using Webignite.Desktop.ViewModels;
using Webignite.Tools.Recording;

namespace Webignite.Desktop.Files;

/// <summary>The recording screen's video work, done by Windows' Media Foundation through Webignite.Tools.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVideo : IVideoTools
{
    public TimeSpan Length(string path) => VideoFile.Length(path);

    public RecordingFrame? FrameAt(string path, TimeSpan at) =>
        VideoFile.FrameAt(path, at) is { } frame ? new RecordingFrame(frame.Pixels, frame.Width, frame.Height) : null;

    public void Trim(string path, string output, TimeSpan start, TimeSpan end) =>
        VideoFile.Trim(path, output, start, end, Webignite.Core.Tools.Recordings.FramesPerSecond);
}
