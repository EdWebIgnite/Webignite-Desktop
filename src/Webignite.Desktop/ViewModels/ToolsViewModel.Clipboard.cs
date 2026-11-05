using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Clipboard history: what was copied while Webignite is open, newest first. Click one to copy it again.
public sealed partial class ToolsViewModel
{
    public ObservableCollection<ClipRow> Clips { get; } = [];

    public bool HasClips => Clips.Count > 0;
    public bool NoClips => Clips.Count == 0;

    [ObservableProperty] public partial string ClipMessage { get; set; } = "";

    /// <summary>Set by the view: puts text back on the clipboard.</summary>
    public Func<string, Task>? CopyClip { get; set; }

    /// <summary>Starts showing the history; the view calls it when shown and <see cref="StopClipboard"/> when closed.</summary>
    public void WatchClipboard()
    {
        services.Clipboard.Changed += LoadClips;
        LoadClips();
    }

    public void StopClipboard()
    {
        services.Clipboard.Changed -= LoadClips;
        Clips.Clear();
    }

    private void LoadClips()
    {
        Clips.Clear();
        foreach (var text in services.Clipboard.Items)
            Clips.Add(new ClipRow(text, this));
        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(NoClips));
    }

    internal async Task CopyAgain(string text)
    {
        if (CopyClip is not { } copy)
            return;
        await copy(text);
        services.Clipboard.Add(text); // to the top, also where Windows doesn't tell us (the screenshot tool)
        ClipMessage = "Copied. Paste it where you need it.";
    }

    internal void Forget(string text)
    {
        services.Clipboard.Remove(text);
        ClipMessage = "";
    }

    [RelayCommand]
    private void ClearClips()
    {
        services.Clipboard.Clear();
        ClipMessage = "Cleared.";
    }
}

/// <summary>One thing copied, shown on one line.</summary>
public sealed partial class ClipRow(string text, ToolsViewModel tools)
{
    public string Text { get; } = text;
    public string Preview { get; } = ClipboardHistory.Preview(text);
    public string Size { get; } = ClipboardHistory.Size(text);

    [RelayCommand]
    private Task Copy() => tools.CopyAgain(Text);

    [RelayCommand]
    private void Forget() => tools.Forget(Text);
}
