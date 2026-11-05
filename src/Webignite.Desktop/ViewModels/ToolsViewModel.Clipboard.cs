using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Clipboard history: what was copied while Webignite is open, text and pictures, newest first. Click one to copy
// it again; the search box finds a copy by its words.
public sealed partial class ToolsViewModel
{
    public ObservableCollection<ClipRow> Clips { get; } = [];

    public bool HasClips => services.Clipboard.Items.Count > 0;
    public bool NoClips => services.Clipboard.Items.Count == 0;
    public bool NothingMatches => HasClips && Clips.Count == 0;

    [ObservableProperty] public partial string ClipMessage { get; set; } = "";
    [ObservableProperty] public partial string ClipSearch { get; set; } = "";

    /// <summary>Set by the view: puts a copy back on the clipboard.</summary>
    public Func<Clip, Task>? CopyClip { get; set; }

    /// <summary>Starts showing the history; the view calls it when shown and <see cref="StopClipboard"/> when closed.</summary>
    public void WatchClipboard()
    {
        services.Clipboard.Changed += LoadClips;
        LoadClips();
    }

    public void StopClipboard()
    {
        services.Clipboard.Changed -= LoadClips;
        ClearClipRows();
    }

    partial void OnClipSearchChanged(string value) => LoadClips();

    private void LoadClips()
    {
        ClearClipRows();
        foreach (var clip in services.Clipboard.Items.Where(c => ClipboardHistory.Matches(c, ClipSearch)))
            Clips.Add(new ClipRow(clip, this));
        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(NoClips));
        OnPropertyChanged(nameof(NothingMatches));
    }

    // Each picture row holds a small picture; they're let go with the rows.
    private void ClearClipRows()
    {
        foreach (var row in Clips)
            row.Dispose();
        Clips.Clear();
    }

    internal async Task CopyAgain(Clip clip)
    {
        if (CopyClip is not { } copy)
            return;
        await copy(clip);
        services.Clipboard.Add(clip); // to the top, also where Windows doesn't tell us (the screenshot tool)
        ClipMessage = "Copied. Paste it where you need it.";
    }

    internal void Forget(Clip clip)
    {
        services.Clipboard.Remove(clip);
        ClipMessage = "";
    }

    [RelayCommand]
    private void ClearClips()
    {
        services.Clipboard.Clear();
        ClipMessage = "Cleared.";
    }
}

/// <summary>One thing copied: a line of its text, or a small picture.</summary>
public sealed partial class ClipRow : IDisposable
{
    private readonly ToolsViewModel tools;

    public ClipRow(Clip clip, ToolsViewModel tools)
    {
        this.tools = tools;
        Clip = clip;
        Preview = clip.Text is { } text ? ClipboardHistory.Preview(text) : "";
        Size = ClipboardHistory.Size(clip);
        if (clip.Picture is { } png)
        {
            using var stream = new MemoryStream(png);
            Thumbnail = Bitmap.DecodeToHeight(stream, 64);
        }
    }

    public Clip Clip { get; }
    public string Preview { get; }
    public string Size { get; }
    public Bitmap? Thumbnail { get; private set; }
    public bool IsPicture => Clip.IsPicture;
    public bool IsText => !Clip.IsPicture;

    [RelayCommand]
    private Task Copy() => tools.CopyAgain(Clip);

    [RelayCommand]
    private void Forget() => tools.Forget(Clip);

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
