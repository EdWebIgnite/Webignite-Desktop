using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

// Settings, Look and feel, the dock: whether it floats over the page, and what sits left and right of it.
public sealed partial class SettingsDockViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices services;

    public SettingsDockViewModel(AppServices services)
    {
        this.services = services;
        // The same choices can be made by right-clicking the dock, so this follows the look wherever it changes.
        services.Settings.AppearanceChanged += LookChanged;
        Load();
    }

    public void Dispose() => services.Settings.AppearanceChanged -= LookChanged;

    private void LookChanged(AppearanceSettings look) => Load();

    public bool DockFloats => services.Settings.Appearance.DockFloats;
    public bool DockHasStrip => !DockFloats;

    public bool IsDarkDock => services.Settings.Appearance.DockTone == DockTone.Dark;
    public bool IsLightDock => !IsDarkDock;

    /// <summary>Each thing that can sit beside the dock, and where it is now.</summary>
    public ObservableCollection<BarChoiceRow> Rows { get; } = [];

    private BarLayout Bar => DockBar.Shown(services.Settings.Appearance.BarLeft, services.Settings.Appearance.BarRight);

    private void Load()
    {
        var bar = Bar;
        Rows.Clear();
        foreach (var widget in Enum.GetValues<BarWidget>())
            Rows.Add(new BarChoiceRow(widget, bar.Left.Contains(widget), bar.Right.Contains(widget)));
        OnPropertyChanged(nameof(DockFloats));
        OnPropertyChanged(nameof(DockHasStrip));
        OnPropertyChanged(nameof(IsDarkDock));
        OnPropertyChanged(nameof(IsLightDock));
    }

    private void Keep(BarLayout bar) =>
        services.Settings.Appearance = services.Settings.Appearance with { BarLeft = DockBar.Saved(bar.Left), BarRight = DockBar.Saved(bar.Right) };

    [RelayCommand]
    private void Float(string floats) => services.Settings.Appearance = services.Settings.Appearance with { DockFloats = floats == "yes" };

    /// <summary>The dock as dark glass or light.</summary>
    [RelayCommand]
    private void Tone(string tone)
    {
        if (Enum.TryParse<DockTone>(tone, out var value))
            services.Settings.Appearance = services.Settings.Appearance with { DockTone = value };
    }

    [RelayCommand]
    private void PutLeft(BarChoiceRow row) => Keep(DockBar.Add(Bar, row.Widget, BarSide.Left));

    [RelayCommand]
    private void PutRight(BarChoiceRow row) => Keep(DockBar.Add(Bar, row.Widget, BarSide.Right));

    [RelayCommand]
    private void TakeOff(BarChoiceRow row) => Keep(DockBar.Remove(Bar, row.Widget));

    /// <summary>Back to what a new install shows beside the dock.</summary>
    [RelayCommand]
    private void Usual() => services.Settings.Appearance = services.Settings.Appearance with { BarLeft = null, BarRight = null };
}

/// <summary>One thing that can sit beside the dock, in Settings: on the left, on the right, or off.</summary>
public sealed record BarChoiceRow(BarWidget Widget, bool IsLeft, bool IsRight)
{
    public string Name => DockBar.Name(Widget);
    public bool IsOff => !IsLeft && !IsRight;
}
