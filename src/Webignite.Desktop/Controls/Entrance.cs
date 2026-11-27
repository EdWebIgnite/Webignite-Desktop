using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Makes a panel ease in: <c>c:Entrance.OnContent="True"</c> on a ContentControl plays when what it shows changes (a
/// quote opened over the list, another client picked); <c>c:Entrance.OnShow="True"</c> plays when the element comes
/// into view (the list coming back, a row folding open). It fades in and rises a few pixels with full motion, only
/// fades (quicker) with reduced motion, and does nothing with motion off, as the Motion setting says. Only elements
/// marked are watched, by one handler for all of them, so it costs nothing elsewhere; the animation keeps nothing
/// once it has run, so a page that closes is let go.
/// </summary>
public static class Entrance
{
    public static readonly AttachedProperty<bool> OnContentProperty =
        AvaloniaProperty.RegisterAttached<ContentControl, bool>("OnContent", typeof(Entrance));

    public static readonly AttachedProperty<bool> OnShowProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("OnShow", typeof(Entrance));

    static Entrance()
    {
        ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>((control, e) =>
        {
            if (GetOnContent(control) && e.NewValue is not null && !ReferenceEquals(e.OldValue, e.NewValue))
                Play(control);
        });
        Visual.IsVisibleProperty.Changed.AddClassHandler<Visual>((visual, e) =>
        {
            if (e.NewValue is true && GetOnShow(visual))
                Play(visual);
        });
    }

    public static bool GetOnContent(ContentControl control) => control.GetValue(OnContentProperty);

    public static void SetOnContent(ContentControl control, bool value) => control.SetValue(OnContentProperty, value);

    public static bool GetOnShow(Visual visual) => visual.GetValue(OnShowProperty);

    public static void SetOnShow(Visual visual, bool value) => visual.SetValue(OnShowProperty, value);

    /// <summary>Eases a control in, as far as the window's Motion setting allows.</summary>
    public static void Play(Visual visual)
    {
        if (TopLevel.GetTopLevel(visual) is not Window window)
            return;
        var full = window.Classes.Contains("motion-full");
        if (!full && !window.Classes.Contains("motion-reduced"))
            return;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(full ? 220 : 110),
            Easing = new CubicEaseOut(),
            // Its last frame is how the element is anyway, so nothing need hold on once it has run.
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            },
        };
        if (full)
        {
            visual.RenderTransform ??= new TranslateTransform();
            animation.Children[0].Setters.Add(new Setter(TranslateTransform.YProperty, 8d));
            animation.Children[1].Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
        }
        animation.RunAsync(visual).Forget("Easing a panel in");
    }
}
