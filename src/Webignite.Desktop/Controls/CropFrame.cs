using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>Dims the picture round the part being kept while cropping, with a line round that part.</summary>
public sealed class CropFrame : Control
{
    public static readonly StyledProperty<Rect> AreaProperty = AvaloniaProperty.Register<CropFrame, Rect>(nameof(Area));

    static CropFrame() => AffectsRender<CropFrame>(AreaProperty);

    public CropFrame() => IsHitTestVisible = false;

    public Rect Area
    {
        get => GetValue(AreaProperty);
        set => SetValue(AreaProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var all = new Rect(Bounds.Size);
        var dim = Brush("Scrim", Color.FromArgb(0x88, 0, 0, 0));
        var keep = Area;
        if (keep.Width <= 0 || keep.Height <= 0)
        {
            context.FillRectangle(dim, all);
            return;
        }
        context.FillRectangle(dim, new Rect(0, 0, all.Width, keep.Top));
        context.FillRectangle(dim, new Rect(0, keep.Bottom, all.Width, Math.Max(0, all.Height - keep.Bottom)));
        context.FillRectangle(dim, new Rect(0, keep.Top, keep.Left, keep.Height));
        context.FillRectangle(dim, new Rect(keep.Right, keep.Top, Math.Max(0, all.Width - keep.Right), keep.Height));
        context.DrawRectangle(null, new Pen(Brush("Surface", Colors.White), 2), keep);
    }

    private static IBrush Brush(string key, Color fallback) =>
        Application.Current?.TryGetResource(key, null, out var found) == true && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
