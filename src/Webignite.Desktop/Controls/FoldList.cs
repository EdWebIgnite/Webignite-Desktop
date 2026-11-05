using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Controls;

/// <summary>
/// One folding row in a <see cref="FoldList"/>: a header with its name and a one-line summary, and what's in
/// it below when open. Click the header to open or close it; drag it by its grip to move it.
/// </summary>
public sealed class FoldSection : ContentControl
{
    public static readonly StyledProperty<string> KeyProperty = AvaloniaProperty.Register<FoldSection, string>(nameof(Key), "");
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<FoldSection, string>(nameof(Title), "");
    public static readonly StyledProperty<string?> SummaryProperty = AvaloniaProperty.Register<FoldSection, string?>(nameof(Summary));
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<FoldSection, Geometry?>(nameof(Icon));
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<FoldSection, bool>(nameof(IsOpen));
    public static readonly StyledProperty<bool> AttentionProperty = AvaloniaProperty.Register<FoldSection, bool>(nameof(Attention));
    public static readonly StyledProperty<bool> DropAboveProperty = AvaloniaProperty.Register<FoldSection, bool>(nameof(DropAbove));

    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Summary { get => GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    /// <summary>The summary says something needs doing, such as an overdue invoice.</summary>
    public bool Attention { get => GetValue(AttentionProperty); set => SetValue(AttentionProperty, value); }

    /// <summary>A row being dragged would land above this one.</summary>
    public bool DropAbove { get => GetValue(DropAboveProperty); set => SetValue(DropAboveProperty, value); }

    internal event Action<FoldSection>? Toggled;
    internal event Action<FoldSection, PointerPressedEventArgs>? GripPressed;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Header") is { } header)
            header.Click += (_, _) => Toggled?.Invoke(this);
        if (e.NameScope.Find<Control>("PART_Grip") is { } grip)
            grip.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
                    GripPressed?.Invoke(this, args);
            };
    }
}

/// <summary>
/// Folding rows in one card, in the order the person left them, open or closed as they left them. The rows
/// are the children; <see cref="Sections"/> says the order and what's open.
/// </summary>
public sealed class FoldList : StackPanel
{
    public static readonly StyledProperty<SectionsViewModel?> SectionsProperty =
        AvaloniaProperty.Register<FoldList, SectionsViewModel?>(nameof(Sections));

    public SectionsViewModel? Sections { get => GetValue(SectionsProperty); set => SetValue(SectionsProperty, value); }

    private FoldSection? dragging;
    private int dropAt = -1;

    public FoldList()
    {
        AddHandler(PointerMovedEvent, Dragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, Dropped, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble);
    }

    private IEnumerable<FoldSection> Rows => Children.OfType<FoldSection>();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SectionsProperty)
        {
            if (change.OldValue is SectionsViewModel old)
                old.PropertyChanged -= LayoutChanged;
            if (change.NewValue is SectionsViewModel now)
                now.PropertyChanged += LayoutChanged;
            Arrange();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var row in Rows)
        {
            row.Toggled -= Toggle;
            row.Toggled += Toggle;
            row.GripPressed -= Grab;
            row.GripPressed += Grab;
            row.ContextMenu ??= Menu(row);
        }
        Arrange();
    }

    private void LayoutChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Arrange();

    // The rows in the saved order, each open or closed as saved.
    private void Arrange()
    {
        if (Sections is not { } sections)
            return;
        var order = sections.Layout.Order;
        var rows = Rows.OrderBy(r => order.ToList().IndexOf(r.Key) is var i and >= 0 ? i : int.MaxValue).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var at = Children.IndexOf(rows[i]);
            if (at != i)
                Children.Move(at, i);
            rows[i].IsOpen = sections.IsOpen(rows[i].Key);
        }
    }

    private void Toggle(FoldSection row) => Sections?.Toggle(row.Key);

    private ContextMenu Menu(FoldSection row)
    {
        MenuItem Item(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            return item;
        }
        return new ContextMenu
        {
            ItemsSource = new object[]
            {
                Item("Move up", () => Sections?.MoveBy(row.Key, -1)),
                Item("Move down", () => Sections?.MoveBy(row.Key, 1)),
                new Separator(),
                Item("Open them all", () => Sections?.All(true)),
                Item("Close them all", () => Sections?.All(false)),
            },
        };
    }

    // Dragging a row by its grip: a line shows where it will land.

    private void Grab(FoldSection row, PointerPressedEventArgs e)
    {
        dragging = row;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void Dragged(object? sender, PointerEventArgs e)
    {
        if (dragging is null)
            return;
        var y = e.GetPosition(this).Y;
        var rows = Rows.ToList();
        dropAt = rows.Count;
        for (var i = 0; i < rows.Count; i++)
        {
            if (y < rows[i].Bounds.Top + rows[i].Bounds.Height / 2)
            {
                dropAt = i;
                break;
            }
        }
        for (var i = 0; i < rows.Count; i++)
            rows[i].DropAbove = i == dropAt && rows[i] != dragging;
    }

    private void Dropped(object? sender, PointerReleasedEventArgs e)
    {
        if (dragging is not { } row)
            return;
        var at = dropAt;
        EndDrag();
        e.Pointer.Capture(null);
        if (at >= 0)
            Sections?.MoveTo(row.Key, at);
    }

    private void EndDrag()
    {
        dragging = null;
        dropAt = -1;
        foreach (var row in Rows)
            row.DropAbove = false;
    }
}
