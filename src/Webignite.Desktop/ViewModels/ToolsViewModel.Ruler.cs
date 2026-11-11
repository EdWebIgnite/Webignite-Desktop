using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

// Screen ruler: measure anything on the screen, from any program. This window starts it and keeps what was
// measured while it is open.
public sealed partial class ToolsViewModel
{
    public bool CanMeasureScreen => OperatingSystem.IsWindows();

    /// <summary>The shortcut as set in Settings, or how to set one.</summary>
    public string RulerHint => services.Settings.Screenshots.RulerShortcut is { Length: > 0 } keys
        ? $"{keys} does the same from any program."
        : "A shortcut for this can be set in Settings, Screenshots.";

    /// <summary>What was measured since this window opened, newest first.</summary>
    public ObservableCollection<string> Measurements { get; } = [];

    public bool HasMeasurements => Measurements.Count > 0;

    [RelayCommand]
    private void MeasureScreen() => services.Go.MeasureScreen(AddMeasurement);

    /// <summary>Keeps a size that was measured, newest first, the last twelve.</summary>
    public void AddMeasurement(string words)
    {
        Measurements.Insert(0, words);
        while (Measurements.Count > 12)
            Measurements.RemoveAt(Measurements.Count - 1);
        OnPropertyChanged(nameof(HasMeasurements));
    }
}
