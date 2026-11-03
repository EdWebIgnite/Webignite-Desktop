using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A short "Saved" with a tick that shows for a moment after a save, then goes. Bind <see cref="Stamp"/> to a
/// number the view model counts up on each save; every change shows the note again. It fades when Motion allows
/// (Styles/Motion.axaml). The look lives in Styles/Controls.axaml.
/// </summary>
public sealed class SavedNote : TemplatedControl
{
    public static readonly StyledProperty<int> StampProperty = AvaloniaProperty.Register<SavedNote, int>(nameof(Stamp));

    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(1.8);
    private DispatcherTimer? timer;

    public int Stamp { get => GetValue(StampProperty); set => SetValue(StampProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != StampProperty || change.GetNewValue<int>() == 0)
            return;
        Classes.Set("shown", true);
        timer?.Stop();
        timer = new DispatcherTimer { Interval = ShowFor };
        timer.Tick += (_, _) =>
        {
            timer?.Stop();
            Classes.Set("shown", false);
        };
        timer.Start();
    }
}
