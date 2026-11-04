using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Webignite.Core.Writing;
using TextBlock = Webignite.Core.Writing.TextBlock;

namespace Webignite.Desktop.Controls;

/// <summary>
/// The mail editor: formatted writing typed straight onto white paper, as it will arrive. The writing is a
/// <see cref="RichDocument"/>; every change goes through <see cref="RichEditing"/>, which has the tests.
/// This file lays the writing out and draws it; the Input file handles keys, mouse and clipboard, and the
/// Commands file is what the toolbar calls.
/// </summary>
public sealed partial class RichTextEditor : Control
{
    public static readonly StyledProperty<RichDocument?> DocumentProperty =
        AvaloniaProperty.Register<RichTextEditor, RichDocument?>(nameof(Document));

    /// <summary>Goes up by one on every change made here, so a view model can tell something was typed.</summary>
    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<RichTextEditor, int>(nameof(Revision), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<RichTextEditor, Thickness>(nameof(Padding), new Thickness(14, 12));

    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<RichTextEditor, string?>(nameof(Placeholder));

    public RichDocument? Document { get => GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    /// <summary>The caret or selection moved, or the look under it changed: the toolbar refreshes.</summary>
    public event EventHandler? SelectionChanged;

    private const double PointsToPixels = 96.0 / 72.0;
    private const double IndentStep = 24;
    private const double ListGap = 22;
    private const double CellPadX = 8;
    private const double CellPadY = 4;

    private static readonly FontFamily Body = new("Arial");
    private static readonly FontFamily FixedFont = new("Consolas, Courier New, monospace");

    private readonly List<Placed> placed = [];
    private readonly Dictionary<PictureBlock, Bitmap?> pictures = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private bool caretVisible = true;
    private double laidOutWidth = -1;
    private bool dirty = true;
    private TextLayout? placeholderLayout;

    private DocPosition caret;
    private DocPosition anchor;

    /// <summary>Where a slot, or a list marker, a line or a table border, was drawn.</summary>
    private sealed record Placed(Slot? Slot, Rect Bounds, TextLayout? Layout = null, Point Origin = default, TextLayout? Marker = null,
        Point MarkerAt = default, string Kind = "text");

    static RichTextEditor()
    {
        FocusableProperty.OverrideDefaultValue<RichTextEditor>(true);
        CursorProperty.OverrideDefaultValue<RichTextEditor>(new Cursor(StandardCursorType.Ibeam));
        AffectsMeasure<RichTextEditor>(DocumentProperty, PaddingProperty);
    }

    public RichTextEditor()
    {
        blink.Tick += (_, _) =>
        {
            caretVisible = !caretVisible;
            InvalidateVisual();
        };
        ClipToBounds = false;
        ContextRequested += ShowMenu;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            caret = anchor = Document is null ? default : RichEditing.Clamp(Document, caret);
            undo.Clear();
            redo.Clear();
            pending = null;
            ForgetPictures(keepInDocument: true);
            Relayout();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Light by design: the blinking caret and decoded pictures only exist while the editor is on screen.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        blink.Stop();
        ForgetPictures(keepInDocument: false);
        DisposeLayouts();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RestartBlink();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        blink.Stop();
        InvalidateVisual();
    }

    private void RestartBlink()
    {
        caretVisible = true;
        blink.Stop();
        if (IsFocused)
            blink.Start();
        InvalidateVisual();
    }

    /// <summary>Something outside changed the writing (the signature was swapped): lay it out again.</summary>
    public void Refresh()
    {
        if (Document is not null)
            caret = anchor = RichEditing.Clamp(Document, caret);
        Relayout();
    }

    private void Relayout()
    {
        dirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Layout

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        if (dirty || Math.Abs(width - laidOutWidth) > 0.5)
            Layout(width);
        var height = placed.Count == 0 ? 0 : placed.Max(p => p.Bounds.Bottom);
        return new Size(width, Math.Max(height + Padding.Bottom, 80));
    }

    private void Layout(double width)
    {
        DisposeLayouts();
        laidOutWidth = width;
        dirty = false;
        if (Document is not { } document)
            return;
        document.EnsureNotEmpty();
        var slots = RichEditing.Slots(document);
        var left = Padding.Left;
        var content = Math.Max(width - Padding.Left - Padding.Right, 40);
        var y = Padding.Top;
        var slotIndex = 0;
        var numbers = new Dictionary<int, int>();
        TextBlock? previous = null;
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case TextBlock paragraph:
                {
                    var slot = slots[slotIndex++];
                    var before = paragraph.Style switch
                    {
                        BlockStyle.Heading1 => 12,
                        BlockStyle.Heading2 => 10,
                        BlockStyle.Heading3 => 8,
                        _ => 0,
                    };
                    var after = paragraph.Style is BlockStyle.Heading1 or BlockStyle.Heading2 or BlockStyle.Heading3 ? 4 : 0;
                    y += previous is null ? 0 : before;
                    var indent = paragraph.Indent * IndentStep + (paragraph.List != ListKind.None ? ListGap : 0) + (paragraph.Style == BlockStyle.Quote ? 12 : 0);
                    var codePad = paragraph.Style == BlockStyle.Code ? 4 : 0;
                    var layout = TextFor(paragraph, Math.Max(content - indent - 2 * codePad, 20));
                    var origin = new Point(left + indent + codePad, y + codePad);
                    var bounds = new Rect(left, y, content, layout.Height + 2 * codePad);
                    TextLayout? marker = null;
                    var markerAt = default(Point);
                    if (paragraph.List == ListKind.None)
                    {
                        numbers.Clear();
                    }
                    else
                    {
                        foreach (var deeper in numbers.Keys.Where(k => k > paragraph.Indent).ToList())
                            numbers.Remove(deeper);
                        var label = paragraph.List == ListKind.Bullet
                            ? (paragraph.Indent % 3) switch { 0 => "•", 1 => "◦", _ => "▪" }
                            : (numbers[paragraph.Indent] = numbers.GetValueOrDefault(paragraph.Indent) + 1).ToString(CultureInfo.InvariantCulture) + ".";
                        if (paragraph.List == ListKind.Bullet)
                            numbers.Remove(paragraph.Indent);
                        var first = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Format : default;
                        marker = new TextLayout(label, new Typeface(Body), Pixels(first.Size), Brush("PaperText"));
                        markerAt = new Point(origin.X - ListGap + 4, origin.Y);
                    }
                    placed.Add(new Placed(slot, bounds, layout, origin, marker, markerAt, paragraph.Style == BlockStyle.Quote ? "quote" : paragraph.Style == BlockStyle.Code ? "code" : "text"));
                    y = bounds.Bottom + after;
                    previous = paragraph;
                    break;
                }
                case PictureBlock picture:
                {
                    var slot = slots[slotIndex++];
                    var w = Math.Min(picture.Width, content);
                    var h = picture.PixelWidth > 0 ? w * picture.PixelHeight / picture.PixelWidth : picture.Height;
                    var x = picture.Align switch
                    {
                        Alignment.Centre => left + (content - w) / 2,
                        Alignment.Right => left + content - w,
                        _ => left,
                    };
                    y += 4;
                    placed.Add(new Placed(slot, new Rect(x, y, Math.Max(w, 8), Math.Max(h, 8)), Kind: "picture"));
                    y += Math.Max(h, 8) + 4;
                    numbers.Clear();
                    previous = null;
                    break;
                }
                case RuleBlock:
                {
                    var slot = slots[slotIndex++];
                    placed.Add(new Placed(slot, new Rect(left, y, content, 17), Kind: "rule"));
                    y += 17;
                    numbers.Clear();
                    previous = null;
                    break;
                }
                case TableBlock table:
                {
                    y += 4;
                    var columns = Math.Max(table.Columns, 1);
                    var cellWidth = content / columns;
                    var top = y;
                    foreach (var row in table.Rows)
                    {
                        var layouts = row.Select(cell => TextFor(cell, Math.Max(cellWidth - 2 * CellPadX, 10))).ToList();
                        var rowHeight = layouts.Count == 0 ? 20 : layouts.Max(l => l.Height) + 2 * CellPadY;
                        for (var c = 0; c < row.Count; c++)
                        {
                            var slot = slots[slotIndex++];
                            var cellRect = new Rect(left + c * cellWidth, y, cellWidth, rowHeight);
                            placed.Add(new Placed(slot, cellRect, layouts[c], new Point(cellRect.X + CellPadX, y + CellPadY), Kind: "cell"));
                        }
                        y += rowHeight;
                    }
                    placed.Add(new Placed(null, new Rect(left, top, cellWidth * columns, y - top), Kind: "table"));
                    y += 4;
                    numbers.Clear();
                    previous = null;
                    break;
                }
            }
        }
    }

    // One paragraph's words as a laid-out block of text, with each run's look.
    private TextLayout TextFor(TextBlock block, double width)
    {
        var text = block.Text;
        var baseFormat = new RunFormat(Bold: block.Style is BlockStyle.Heading1 or BlockStyle.Heading2 or BlockStyle.Heading3, Fixed: block.Style == BlockStyle.Code);
        var size = HeadingPixels(block.Style) ?? Pixels(TextSize.Normal);
        var align = block.Align switch
        {
            Alignment.Centre => TextAlignment.Center,
            Alignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        var overrides = new List<Avalonia.Utilities.ValueSpan<TextRunProperties>>();
        foreach (var (start, run) in block.Spans())
            overrides.Add(new(start, run.Text.Length, Properties(run.Format, block.Style)));
        // An empty paragraph still needs a line's height, in the size typing there would use.
        var shown = text.Length == 0 ? " " : text;
        var emptySize = text.Length == 0 && pending is { } p && IsCaretBlock(block) ? Pixels(p.Size) : size;
        return new TextLayout(shown, Typeface(baseFormat), text.Length == 0 ? emptySize : size, Brush("PaperText"), align, TextWrapping.Wrap,
            maxWidth: width, textStyleOverrides: overrides.Count == 0 ? null : overrides);
    }

    private bool IsCaretBlock(TextBlock block) =>
        Document is { } d && caret.Slot < RichEditing.Slots(d).Count && ReferenceEquals(RichEditing.Slots(d)[caret.Slot].Block, block);

    private GenericTextRunProperties Properties(RunFormat f, BlockStyle style)
    {
        var heading = style is BlockStyle.Heading1 or BlockStyle.Heading2 or BlockStyle.Heading3;
        var face = Typeface(f with { Bold = f.Bold || heading, Fixed = f.Fixed || style == BlockStyle.Code });
        var size = heading && f.Size == TextSize.Normal ? HeadingPixels(style)!.Value : Pixels(f.Size);
        var decorations = f.Underline || f.Link is not null ? TextDecorations.Underline : null;
        IBrush foreground = f.Colour is { } c && Color.TryParse(c, out var colour) ? new SolidColorBrush(colour)
            : f.Link is not null ? Brush("PaperLink") : Brush("PaperText");
        IBrush? background = f.Highlight is { } h && Color.TryParse(h, out var shade) ? new SolidColorBrush(shade) : null;
        return new GenericTextRunProperties(face, size, decorations, foreground, background, BaselineAlignment.Baseline, CultureInfo.CurrentCulture, null);
    }

    private static Typeface Typeface(RunFormat f) =>
        new(f.Fixed ? FixedFont : Body, f.Italic ? FontStyle.Italic : FontStyle.Normal, f.Bold ? FontWeight.Bold : FontWeight.Normal);

    private static double Pixels(TextSize size) => TextSizes.Points(size) * PointsToPixels;

    private static double? HeadingPixels(BlockStyle style) => style switch
    {
        BlockStyle.Heading1 => 18 * PointsToPixels,
        BlockStyle.Heading2 => 15 * PointsToPixels,
        BlockStyle.Heading3 => 12 * PointsToPixels,
        _ => null,
    };

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Black;

    private void DisposeLayouts()
    {
        foreach (var p in placed)
        {
            p.Layout?.Dispose();
            p.Marker?.Dispose();
        }
        placed.Clear();
        placeholderLayout?.Dispose();
        placeholderLayout = null;
        laidOutWidth = -1;
        dirty = true;
    }

    private Bitmap? PictureFor(PictureBlock block)
    {
        if (pictures.TryGetValue(block, out var cached))
            return cached;
        Bitmap? bitmap = null;
        try
        {
            using var stream = new MemoryStream(block.Data);
            bitmap = Bitmap.DecodeToWidth(stream, Math.Max(block.Width, 1));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
        }
        pictures[block] = bitmap;
        return bitmap;
    }

    // Pictures no longer in the writing are let go straight away; all of them when the editor closes.
    private void ForgetPictures(bool keepInDocument)
    {
        var keep = keepInDocument && Document is { } d ? d.Blocks.OfType<PictureBlock>().ToHashSet(ReferenceEqualityComparer.Instance) : [];
        foreach (var (block, bitmap) in pictures.ToList())
        {
            if (keep.Contains(block))
                continue;
            bitmap?.Dispose();
            pictures.Remove(block);
        }
    }

    // Drawing

    public override void Render(DrawingContext context)
    {
        if (dirty)
        {
            Layout(Bounds.Width);
        }
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var (start, end) = Selection;
        var hasSelection = start != end;
        var selectionBrush = Brush("PaperSelection");
        var line = new Pen(Brush("PaperLine"), 1);
        var slotIndex = 0;
        foreach (var p in placed)
        {
            if (p.Slot is null)
            {
                if (p.Kind == "table")
                    context.DrawRectangle(null, line, p.Bounds);
                continue;
            }
            var index = slotIndex++;
            switch (p.Kind)
            {
                case "picture" when p.Slot.Block is PictureBlock picture:
                    if (PictureFor(picture) is { } bitmap)
                        context.DrawImage(bitmap, p.Bounds);
                    else
                        context.DrawRectangle(Brush("PaperShade"), line, p.Bounds);
                    if (hasSelection && index >= start.Slot && index <= end.Slot && (index != start.Slot || start.Offset == 0) && (index != end.Slot || end.Offset == 1))
                        context.DrawRectangle(null, new Pen(selectionBrush, 3), p.Bounds.Inflate(1));
                    break;
                case "rule":
                    context.DrawLine(line, new Point(p.Bounds.Left, p.Bounds.Center.Y), new Point(p.Bounds.Right, p.Bounds.Center.Y));
                    break;
                default:
                    if (p.Kind == "cell")
                        context.DrawRectangle(null, line, p.Bounds);
                    if (p.Kind == "quote")
                        context.FillRectangle(Brush("PaperLine"), new Rect(p.Origin.X - 12, p.Bounds.Top, 2, p.Bounds.Height));
                    if (p.Kind == "code")
                        context.FillRectangle(Brush("PaperShade"), p.Bounds);
                    if (hasSelection && p.Layout is { } layout && index >= start.Slot && index <= end.Slot && p.Slot.Text is { } text)
                    {
                        var from = index == start.Slot ? start.Offset : 0;
                        var to = index == end.Slot ? end.Offset : text.Length;
                        // A selection running on past the end of a paragraph shows a little of the line ending too.
                        foreach (var r in layout.HitTestTextRange(from, Math.Max(to - from, 0)))
                            context.FillRectangle(selectionBrush, r.Translate(p.Origin));
                        if (index != end.Slot && to == text.Length)
                        {
                            var endRect = layout.HitTestTextPosition(text.Length).Translate(p.Origin);
                            context.FillRectangle(selectionBrush, new Rect(endRect.X, endRect.Y, 6, endRect.Height));
                        }
                    }
                    p.Layout?.Draw(context, p.Origin);
                    p.Marker?.Draw(context, p.MarkerAt);
                    break;
            }
        }

        if (Document is { } document && document.IsBlank && !document.HasSignature && !string.IsNullOrEmpty(Placeholder) && placed.FirstOrDefault() is { } firstPlaced)
        {
            placeholderLayout ??= new TextLayout(Placeholder, new Typeface(Body), Pixels(TextSize.Normal), Brush("PaperLine"));
            placeholderLayout.Draw(context, firstPlaced.Origin);
        }

        if (IsFocused && caretVisible && !hasSelection && CaretRect() is { } c)
            context.FillRectangle(Brush("PaperText"), new Rect(c.X, c.Y, 1.5, c.Height));
    }

    /// <summary>The caret's rectangle in the editor, for drawing and for scrolling it into view.</summary>
    private Rect? CaretRect()
    {
        if (PlacedAt(caret.Slot) is not { } p)
            return null;
        if (p.Layout is { } layout)
        {
            var r = layout.HitTestTextPosition(caret.Offset).Translate(p.Origin);
            return new Rect(r.X, r.Y, 1.5, Math.Max(r.Height, 14));
        }
        var x = caret.Offset == 0 ? p.Bounds.Left - 2 : p.Bounds.Right + 1;
        return new Rect(x, p.Bounds.Top, 1.5, p.Bounds.Height);
    }

    private Placed? PlacedAt(int slot)
    {
        var i = 0;
        foreach (var p in placed)
        {
            if (p.Slot is null)
                continue;
            if (i == slot)
                return p;
            i++;
        }
        return null;
    }

    private IEnumerable<(int Slot, Placed Placed)> PlacedSlots()
    {
        var i = 0;
        foreach (var p in placed)
        {
            if (p.Slot is not null)
                yield return (i++, p);
        }
    }

    /// <summary>The place in the writing under a point.</summary>
    private DocPosition PositionAt(Point point)
    {
        (int Slot, Placed Placed)? best = null;
        var bestDistance = double.MaxValue;
        foreach (var (slot, p) in PlacedSlots())
        {
            var dy = point.Y < p.Bounds.Top ? p.Bounds.Top - point.Y : point.Y > p.Bounds.Bottom ? point.Y - p.Bounds.Bottom : 0;
            var dx = point.X < p.Bounds.Left ? p.Bounds.Left - point.X : point.X > p.Bounds.Right ? point.X - p.Bounds.Right : 0;
            var distance = dy * 1000 + dx;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (slot, p);
            }
        }
        if (best is not { } found)
            return default;
        if (found.Placed.Layout is { } layout)
        {
            var local = point - found.Placed.Origin;
            var hit = layout.HitTestPoint(local);
            var length = found.Placed.Slot!.Length;
            return new DocPosition(found.Slot, Math.Clamp(hit.TextPosition, 0, length));
        }
        return new DocPosition(found.Slot, point.X < found.Placed.Bounds.Center.X ? 0 : 1);
    }
}
