using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A small turning arc that says "working on it". It only asks for frames while it is on screen,
/// so a hidden spinner costs nothing. With Motion set to Off it stands still.
/// </summary>
public sealed class Spinner : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Spinner>();
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Spinner, double>(nameof(Size), 14);

    /// <summary>Set from the Motion setting. True stops every spinner turning.</summary>
    public static bool Still { get; set; }

    private double angle;
    private TimeSpan? last;
    private bool asked;

    static Spinner()
    {
        AffectsRender<Spinner>(ForegroundProperty);
        AffectsMeasure<Spinner>(SizeProperty);
    }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Ask();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            Ask();
    }

    private void Ask()
    {
        if (asked || Still || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
            return;
        asked = true;
        top.RequestAnimationFrame(Tick);
    }

    private void Tick(TimeSpan now)
    {
        asked = false;
        if (last is { } before)
            angle = (angle + (now - before).TotalSeconds * 300) % 360;
        last = now;
        InvalidateVisual();
        if (IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null)
            Ask();
        else
            last = null;
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
            return;
        var size = Size;
        var radius = size / 2 - 1.5;
        var centre = new Point(size / 2, size / 2);
        var faint = brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.25) : brush;
        context.DrawEllipse(null, new Pen(faint, 2), centre, radius, radius);

        var start = angle * Math.PI / 180;
        var end = start + Math.PI * 0.6;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(centre + new Vector(Math.Cos(start), Math.Sin(start)) * radius, false);
            g.ArcTo(centre + new Vector(Math.Cos(end), Math.Sin(end)) * radius, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 2, lineCap: PenLineCap.Round), geometry);
        // Shown again after a parent was hidden: start turning again.
        Ask();
    }
}
