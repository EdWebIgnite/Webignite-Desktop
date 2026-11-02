using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace Webignite.Desktop;

/// <summary>How much the app moves: fades and small slides, only colour fades, or nothing at all.</summary>
public enum Motion
{
    Full,
    Reduced,
    Off,
}

/// <summary>Look settings kept on this PC.</summary>
public sealed record AppearanceSettings
{
    public Motion Motion { get; init; } = Motion.Full;
}

/// <summary>
/// A new page fades in and rises a few pixels while the old one fades out. With reduced motion it only fades,
/// quicker. The window's Motion setting picks which, or none.
/// </summary>
public sealed class FadeRise(TimeSpan duration, double rise) : IPageTransition
{
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var easing = new CubicEaseOut();
        var tasks = new List<Task>();
        if (from is not null)
        {
            tasks.Add(new Animation
            {
                Duration = duration / 2,
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                },
            }.RunAsync(from, cancellationToken));
        }
        if (to is not null)
        {
            to.IsVisible = true;
            var fadeIn = new Animation
            {
                Duration = duration,
                Easing = easing,
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                },
            };
            if (rise > 0)
            {
                to.RenderTransform = new TranslateTransform();
                fadeIn.Children[0].Setters.Add(new Setter(TranslateTransform.YProperty, rise));
                fadeIn.Children[1].Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
            }
            tasks.Add(fadeIn.RunAsync(to, cancellationToken));
        }
        await Task.WhenAll(tasks);
        if (from is not null && !cancellationToken.IsCancellationRequested)
            from.IsVisible = false;
    }

    /// <summary>The page change for a Motion setting, or null for none.</summary>
    public static IPageTransition? For(Motion motion) => motion switch
    {
        Motion.Full => new FadeRise(TimeSpan.FromMilliseconds(240), 10),
        Motion.Reduced => new FadeRise(TimeSpan.FromMilliseconds(120), 0),
        _ => null,
    };
}
