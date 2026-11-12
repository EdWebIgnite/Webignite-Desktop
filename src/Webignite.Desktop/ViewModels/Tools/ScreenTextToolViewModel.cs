using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

// Text from the screen: drag over words anywhere on the screen (a picture, a video, a PDF that won't let you
// select) and they come back as text, read on this PC by Windows.
public sealed partial class ScreenTextToolViewModel : ViewModelBase
{
    private readonly AppServices services;

    public ScreenTextToolViewModel(AppServices services)
    {
        this.services = services;
    }

    public bool CanReadScreen => OperatingSystem.IsWindows() && services.ReadPictureText is not null;

    public string ScreenTextHint => services.Settings.Screenshots.TextShortcut is { Length: > 0 } keys
        ? $"{keys} does the same from any program."
        : "A shortcut for this can be set in Settings, Screenshots.";

    /// <summary>The words last read, to check, fix and copy from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenText))]
    public partial string ScreenTextRead { get; set; } = "";

    public bool HasScreenText => ScreenTextRead.Length > 0;

    [RelayCommand]
    private void ReadScreen() => services.Go.ReadScreenText(text => ScreenTextRead = text);
}
