using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A row of tabs drawn as one part: an icon and a name each, the one for the page on screen lit. The areas across
/// the top of the window (Mail, Business, Planner, Computer, Tools) are one, and the tabs of a page that is several
/// pages in one (Billing) another. The areas are on screen from the first frame, so the row draws its tabs itself
/// instead of holding a button for each, which keeps start-up light. Its colours come from its style.
/// </summary>
public sealed class AreaBar : Control
{
    private const double PadX = 14, PadY = 7, IconSize = 15, IconGap = 8, Between = 4, Corner = 8;

    public static readonly StyledProperty<IReadOnlyList<BarTab>?> TabsProperty = AvaloniaProperty.Register<AreaBar, IReadOnlyList<BarTab>?>(nameof(Tabs));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<AreaBar>();
    public static readonly StyledProperty<IBrush?> OnForegroundProperty = AvaloniaProperty.Register<AreaBar, IBrush?>(nameof(OnForeground));
    public static readonly StyledProperty<IBrush?> OnBackgroundProperty = AvaloniaProperty.Register<AreaBar, IBrush?>(nameof(OnBackground));

    private readonly List<Rect> places = [];
    private IReadOnlyList<BarTab>? watched;
    private int hover = -1;

    static AreaBar()
    {
        AffectsRender<AreaBar>(ForegroundProperty, OnForegroundProperty, OnBackgroundProperty);
        AffectsMeasure<AreaBar>(TabsProperty, TextElement.FontSizeProperty, TextElement.FontFamilyProperty);
    }

    public AreaBar() => Cursor = new Cursor(StandardCursorType.Hand);

    public IReadOnlyList<BarTab>? Tabs { get => GetValue(TabsProperty); set => SetValue(TabsProperty, value); }

    /// <summary>The colour of a tab's icon and name.</summary>
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>The colour of the lit tab's icon and name.</summary>
    public IBrush? OnForeground { get => GetValue(OnForegroundProperty); set => SetValue(OnForegroundProperty, value); }

    /// <summary>What the lit tab sits on; the tab under the pointer gets a fainter wash of it.</summary>
    public IBrush? OnBackground { get => GetValue(OnBackgroundProperty); set => SetValue(OnBackgroundProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TabsProperty && VisualRoot is not null)
            Watch(Tabs);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(Tabs);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    // A tab lights up or goes dark when the page on screen changes: drawn again then. Let go of when the bar is.
    private void Watch(IReadOnlyList<BarTab>? tabs)
    {
        if (ReferenceEquals(watched, tabs))
            return;
        foreach (var tab in watched ?? [])
            tab.PropertyChanged -= TabChanged;
        watched = tabs;
        foreach (var tab in watched ?? [])
            tab.PropertyChanged += TabChanged;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void TabChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A name that changed (a count after it) changes the tab's width.
        if (e.PropertyName == nameof(BarTab.Title))
            InvalidateMeasure();
        InvalidateVisual();
    }

    private FormattedText Words(string text, IBrush? brush) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.SemiBold), TextElement.GetFontSize(this), brush);

    protected override Size MeasureOverride(Size availableSize)
    {
        places.Clear();
        double x = 0, height = 0;
        foreach (var tab in Tabs ?? [])
        {
            var words = Words(tab.Title, null);
            var width = PadX + IconSize + IconGap + Math.Ceiling(words.Width) + PadX;
            height = Math.Max(height, Math.Ceiling(Math.Max(words.Height, IconSize)) + PadY * 2);
            places.Add(new Rect(x, 0, width, height));
            x += width + Between;
        }
        for (var i = 0; i < places.Count; i++)
            places[i] = places[i].WithHeight(height);
        return new Size(Math.Max(0, x - Between), height);
    }

    public override void Render(DrawingContext context)
    {
        var tabs = Tabs;
        if (tabs is null)
            return;
        for (var i = 0; i < tabs.Count && i < places.Count; i++)
        {
            var tab = tabs[i];
            var place = places[i];
            if (OnBackground is { } back && (tab.IsOn || i == hover))
            {
                using (context.PushOpacity(tab.IsOn ? 1 : 0.6))
                    context.DrawRectangle(back, null, place, Corner, Corner);
            }
            var brush = tab.IsOn ? OnForeground ?? Foreground : Foreground;
            if (brush is null)
                continue;
            if (IconConverter.Instance.Convert(tab.Icon, typeof(Geometry), null, CultureInfo.InvariantCulture) is Geometry icon)
            {
                var scale = IconSize / 24;
                using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(place.X + PadX, place.Y + (place.Height - IconSize) / 2)))
                    context.DrawGeometry(null, new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon);
            }
            var words = Words(tab.Title, brush);
            context.DrawText(words, new Point(place.X + PadX + IconSize + IconGap, place.Y + (place.Height - words.Height) / 2));
        }
    }

    /// <summary>The tab at a point in the bar, or null between tabs.</summary>
    public BarTab? TabAt(Point point)
    {
        var at = places.FindIndex(place => place.Contains(point));
        return at >= 0 && Tabs is { } tabs && at < tabs.Count ? tabs[at] : null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Hover(places.FindIndex(place => place.Contains(e.GetPosition(this))));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Hover(-1);
    }

    private void Hover(int at)
    {
        if (at == hover)
            return;
        hover = at;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TabAt(e.GetPosition(this)) is { } tab)
        {
            tab.PickCommand.Execute(null);
            e.Handled = true;
        }
    }
}
