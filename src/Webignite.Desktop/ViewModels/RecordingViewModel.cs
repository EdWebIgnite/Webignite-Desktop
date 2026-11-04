using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

/// <summary>A frame of a recording: top row first, four bytes a pixel.</summary>
public sealed record RecordingFrame(byte[] Pixels, int Width, int Height);

/// <summary>What the recording screen needs from the video library: how long, a frame, a trimmed copy.</summary>
public interface IVideoTools
{
    TimeSpan Length(string path);
    RecordingFrame? FrameAt(string path, TimeSpan at);
    void Trim(string path, string output, TimeSpan start, TimeSpan end);
}

/// <summary>
/// A recording just made, in Mark-up: pick where it starts and ends with a frame to see, then save it in the
/// client's folder, save it elsewhere, mail it, or throw it away. Until then it waits in a temporary folder.
/// </summary>
public sealed partial class RecordingViewModel : ViewModelBase, IDisposable
{
    private readonly IVideoTools video;
    private readonly MarkupViewModel owner;
    private readonly AppServices services;
    private int previewVersion;

    public RecordingViewModel(string path, TimeSpan length, IVideoTools video, MarkupViewModel owner, AppServices services, string? soundProblem)
    {
        Path = path;
        Length = length;
        this.video = video;
        this.owner = owner;
        this.services = services;
        End = length.TotalSeconds;
        SoundProblem = soundProblem;
        ShowFrame(TimeSpan.Zero);
    }

    public string Path { get; }
    public TimeSpan Length { get; }
    public double Seconds => Length.TotalSeconds;
    public string? SoundProblem { get; }
    public bool HasSoundProblem => SoundProblem is not null;
    public string LengthText => Recordings.Clock(Length);
    public string SizeText => File.Exists(Path) ? Webignite.Core.Files.FileSizes.Format(new FileInfo(Path).Length) : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartText), nameof(KeptText))]
    public partial double Start { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndText), nameof(KeptText))]
    public partial double End { get; set; }

    public string StartText => Recordings.Clock(TimeSpan.FromSeconds(Start));
    public string EndText => Recordings.Clock(TimeSpan.FromSeconds(End));

    public string KeptText
    {
        get
        {
            var (start, end) = Kept();
            return Recordings.IsTrimmed(start, end, Length)
                ? $"Keeping {Recordings.Clock(start)} to {Recordings.Clock(end)}, {Recordings.Clock(end - start)} of {LengthText}"
                : $"All of it, {LengthText}";
        }
    }

    [ObservableProperty] public partial Bitmap? Frame { get; private set; }
    [ObservableProperty] public partial string FrameText { get; private set; } = "";
    [ObservableProperty] public partial bool Working { get; private set; }

    /// <summary>Where it was saved, once it has been.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    public partial string? SavedPath { get; private set; }

    public bool HasSaved => SavedPath is not null;

    private (TimeSpan Start, TimeSpan End) Kept() => Recordings.Trim(TimeSpan.FromSeconds(Start), TimeSpan.FromSeconds(End), Length);

    partial void OnStartChanged(double value) => ShowFrame(TimeSpan.FromSeconds(value));

    partial void OnEndChanged(double value) => ShowFrame(TimeSpan.FromSeconds(value));

    // The frame at the handle last moved, drawn off the screen's thread; a newer move wins over an older one.
    private void ShowFrame(TimeSpan at)
    {
        var version = ++previewVersion;
        FrameText = Recordings.Clock(at);
        Load(at, version).Forget("Showing a frame of the recording");
    }

    private async Task Load(TimeSpan at, int version)
    {
        var frame = await Task.Run(() => video.FrameAt(Path, at < Length ? at : Length - TimeSpan.FromMilliseconds(100)));
        if (version != previewVersion || frame is null)
            return;
        var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
            for (var y = 0; y < frame.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, y * frame.Width * 4, buffer.Address + y * buffer.RowBytes, frame.Width * 4);
        var old = Frame;
        Frame = bitmap;
        old?.Dispose();
    }

    /// <summary>The kept part written to a file: a trimmed copy, or the recording itself moved there when nothing was cut.</summary>
    public async Task<bool> SaveTo(string target)
    {
        var (start, end) = Kept();
        Working = true;
        try
        {
            await Task.Run(() =>
            {
                if (Recordings.IsTrimmed(start, end, Length))
                    video.Trim(Path, target, start, end);
                else
                    File.Copy(Path, target, overwrite: true);
            });
            SavedPath = target;
            owner.Say($"Saved {System.IO.Path.GetFileName(target)} in {System.IO.Path.GetDirectoryName(target)}.");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            owner.Say("Couldn't save it: " + e.Message, problem: true);
            return false;
        }
        finally
        {
            Working = false;
        }
    }

    /// <summary>Saved in the client's folder when they have one, else in Screenshots.</summary>
    [RelayCommand]
    private async Task Save()
    {
        var folder = owner.SaveFolder();
        Directory.CreateDirectory(folder);
        var name = Webignite.Core.Files.FileNames.Unique(Recordings.FileName(owner.Client?.Name, DateTime.Now), false,
            Directory.EnumerateFileSystemEntries(folder).Select(System.IO.Path.GetFileName).OfType<string>());
        await SaveTo(System.IO.Path.Combine(folder, name));
    }

    [RelayCommand]
    private void ShowSaved()
    {
        if (SavedPath is { } saved && System.IO.Path.GetDirectoryName(saved) is { } folder)
            services.Go.Folder(folder);
    }

    /// <summary>A mail with the recording attached; a big one is saved first and the mail says so.</summary>
    [RelayCommand]
    private async Task Email()
    {
        if (services.Drafts.EverydayMailbox is not { } mailbox)
        {
            owner.Say(MailDrafts.NoMailbox, problem: true);
            return;
        }
        if (SavedPath is null)
            await Save();
        if (SavedPath is not { } saved)
            return;
        var size = new FileInfo(saved).Length;
        var first = owner.Client?.ContactPerson is { Length: > 0 } person ? person.Split(' ')[0] : owner.Client?.Name ?? "there";
        var opened = services.Drafts.Open(new MailDraft(mailbox, owner.Client?.Email, "A screen recording for you",
            $"Hi {first},\n\nHere's a short screen recording ({Recordings.Clock(Kept().End - Kept().Start)}).\n\nThanks,\n{services.Settings.Xero.TenantName ?? "Webignite"}")
        {
            Attachments = [new OutgoingAttachment(System.IO.Path.GetFileName(saved), size, FilePath: saved)],
            Note = size > 20 * 1024 * 1024 ? "It's over 20 MB, which many mailboxes refuse. A link from OneDrive or Google Drive is safer." : null,
        });
        if (!opened)
            owner.Say(MailDrafts.AnotherOpen, problem: true);
    }

    /// <summary>Back to Mark-up; the recording is thrown away unless it was saved.</summary>
    [RelayCommand]
    private void Close() => owner.CloseRecording();

    public void Dispose()
    {
        previewVersion++;
        Frame?.Dispose();
        Frame = null;
        try
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Webignite.Data.AppLog.Write("Tidying a recording", e);
        }
    }
}
