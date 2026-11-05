namespace Webignite.Core.Layout;

/// <summary>What a widget on one of the person's own pages shows.</summary>
public enum WidgetKind
{
    MyTasks,
    Project,
    Today,
    NeedsYou,
    Mail,
    Client,
    Month,
    Note,
    Calculator,
    Timer,
    Clipboard,
    Colours,
}

/// <summary>
/// A widget's place on a page's grid: <see cref="MyPages.Columns"/> columns across, rows going down as far as
/// needed. <see cref="Target"/> is the project or client it shows; <see cref="Text"/> is a note's words.
/// </summary>
public sealed record Widget
{
    public required string Id { get; init; }
    public WidgetKind Kind { get; init; }
    public int Column { get; init; }
    public int Row { get; init; }
    public int Width { get; init; } = 4;
    public int Height { get; init; } = 4;
    public string? Target { get; init; }
    public string? Text { get; init; }
}

/// <summary>A page the person made: a name in the sidebar and the widgets on it.</summary>
public sealed record MyPage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<Widget> Widgets { get; init; } = [];
}

/// <summary>
/// The rules for the person's own pages. Widgets sit on a grid of twelve columns; one dropped or grown onto
/// others pushes them down, and everything then slides up into any space left above it, so a page never has
/// holes or overlaps.
/// </summary>
public static class MyPages
{
    public const int Columns = 12;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public static string Label(WidgetKind kind) => kind switch
    {
        WidgetKind.MyTasks => "My tasks",
        WidgetKind.Project => "A project",
        WidgetKind.Today => "Today",
        WidgetKind.NeedsYou => "Needs you",
        WidgetKind.Mail => "Mail inbox",
        WidgetKind.Client => "A client",
        WidgetKind.Month => "This month",
        WidgetKind.Note => "Note",
        WidgetKind.Calculator => "Calculator",
        WidgetKind.Timer => "Timer",
        WidgetKind.Clipboard => "Clipboard history",
        _ => "Colours",
    };

    /// <summary>Widgets that show one project or one client, picked when added.</summary>
    public static bool NeedsTarget(WidgetKind kind) => kind is WidgetKind.Project or WidgetKind.Client;

    /// <summary>The size a widget starts at, in columns and rows.</summary>
    public static (int Width, int Height) StartSize(WidgetKind kind) => kind switch
    {
        WidgetKind.Project or WidgetKind.Month => (6, 5),
        WidgetKind.NeedsYou or WidgetKind.Mail => (6, 4),
        WidgetKind.Calculator => (3, 6),
        WidgetKind.Timer => (3, 2),
        WidgetKind.Note => (3, 3),
        _ => (4, 4),
    };

    /// <summary>The smallest a widget can be and still show something useful.</summary>
    public static (int Width, int Height) LeastSize(WidgetKind kind) => kind switch
    {
        WidgetKind.Calculator => (3, 5),
        WidgetKind.Month => (4, 4),
        WidgetKind.Timer => (2, 2),
        _ => (2, 2),
    };

    public static int MostHeight => 16;

    public static MyPage NewPage(IEnumerable<MyPage> existing)
    {
        var names = existing.Select(p => p.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var name = "My page";
        for (var n = 2; names.Contains(name); n++)
            name = $"My page {n}";
        return new MyPage { Id = NewId(), Name = name };
    }

    /// <summary>A new name for a page; empty keeps the old one.</summary>
    public static MyPage Rename(MyPage page, string name) =>
        string.IsNullOrWhiteSpace(name) ? page : page with { Name = name.Trim() };

    /// <summary>A new widget of a kind, at its usual size, in the first free spot from the top left.</summary>
    public static MyPage Add(MyPage page, WidgetKind kind, string? target = null)
    {
        var (width, height) = StartSize(kind);
        var spot = FreeSpot(page.Widgets, width, height);
        var widget = new Widget { Id = NewId(), Kind = kind, Column = spot.Column, Row = spot.Row, Width = width, Height = height, Target = target };
        return page with { Widgets = [.. page.Widgets, widget] };
    }

    public static MyPage Remove(MyPage page, string id) =>
        page with { Widgets = Settle([.. page.Widgets.Where(w => w.Id != id)], null) };

    /// <summary>Moves a widget so its top left is at a cell; it stays inside the columns, and those in its way go down.</summary>
    public static MyPage Move(MyPage page, string id, int column, int row) => Change(page, id, w => w with
    {
        Column = Math.Clamp(column, 0, Columns - w.Width),
        Row = Math.Max(0, row),
    });

    /// <summary>Makes a widget a new size, kept between its least size and the edge of the page.</summary>
    public static MyPage Resize(MyPage page, string id, int width, int height) => Change(page, id, w =>
    {
        var least = LeastSize(w.Kind);
        return w with
        {
            Width = Math.Clamp(width, least.Width, Columns - w.Column),
            Height = Math.Clamp(height, least.Height, MostHeight),
        };
    });

    public static MyPage SetText(MyPage page, string id, string text) => page with
    {
        Widgets = [.. page.Widgets.Select(w => w.Id == id ? w with { Text = text } : w)],
    };

    /// <summary>
    /// A saved page made sound: sizes and places kept on the grid, widgets that overlap moved down, and gaps above
    /// closed, so a page saved by an older version (or edited by hand) still lays out.
    /// </summary>
    public static MyPage Reconcile(MyPage page) => page with
    {
        Name = string.IsNullOrWhiteSpace(page.Name) ? "My page" : page.Name,
        Widgets = Settle([.. page.Widgets.Select(Fit)], null),
    };

    private static Widget Fit(Widget w)
    {
        var least = LeastSize(w.Kind);
        var width = Math.Clamp(w.Width, least.Width, Columns);
        return w with
        {
            Width = width,
            Height = Math.Clamp(w.Height, least.Height, MostHeight),
            Column = Math.Clamp(w.Column, 0, Columns - width),
            Row = Math.Max(0, w.Row),
        };
    }

    private static MyPage Change(MyPage page, string id, Func<Widget, Widget> change)
    {
        var changed = page.Widgets.FirstOrDefault(w => w.Id == id);
        if (changed is null)
            return page;
        changed = change(changed);
        return page with { Widgets = Settle([.. page.Widgets.Select(w => w.Id == id ? changed : w)], changed) };
    }

    public static bool Overlap(Widget a, Widget b) =>
        a.Column < b.Column + b.Width && b.Column < a.Column + a.Width && a.Row < b.Row + b.Height && b.Row < a.Row + a.Height;

    /// <summary>
    /// The one just moved or grown keeps its place; the others, top to bottom, go below anything they now
    /// overlap; then each slides up as far as it can. The order of the list is kept.
    /// </summary>
    private static List<Widget> Settle(List<Widget> widgets, Widget? fixedOne)
    {
        var placed = new List<Widget>();
        if (fixedOne is not null)
            placed.Add(fixedOne);
        foreach (var widget in widgets.Where(w => w.Id != fixedOne?.Id).OrderBy(w => w.Row).ThenBy(w => w.Column))
        {
            var w = widget;
            // One that was above where the widget landed goes over it if there's room, so dragging a widget
            // down past another swaps them rather than sliding straight back.
            if (fixedOne is not null && Overlap(fixedOne, w) && w.Row < fixedOne.Row
                && w with { Row = Math.Max(0, fixedOne.Row - w.Height) } is var above && !placed.Any(p => Overlap(p, above)))
                w = above;
            while (placed.FirstOrDefault(p => Overlap(p, w)) is { } blocker)
                w = w with { Row = blocker.Row + blocker.Height };
            placed.Add(w);
        }
        // Slide up, top ones first, into any room above.
        var settled = new List<Widget>();
        foreach (var widget in placed.OrderBy(w => w.Row).ThenBy(w => w.Column))
        {
            var w = widget;
            while (w.Row > 0 && !settled.Any(s => Overlap(s, w with { Row = w.Row - 1 })))
                w = w with { Row = w.Row - 1 };
            settled.Add(w);
        }
        var byId = settled.ToDictionary(w => w.Id);
        return [.. widgets.Select(w => byId[w.Id])];
    }

    private static (int Column, int Row) FreeSpot(IReadOnlyList<Widget> widgets, int width, int height)
    {
        for (var row = 0; ; row++)
        {
            for (var column = 0; column + width <= Columns; column++)
            {
                var trial = new Widget { Id = "", Column = column, Row = row, Width = width, Height = height };
                if (!widgets.Any(w => Overlap(w, trial)))
                    return (column, row);
            }
        }
    }

    /// <summary>How many rows the page takes, for the height of its grid.</summary>
    public static int Rows(MyPage page) => page.Widgets.Count == 0 ? 0 : page.Widgets.Max(w => w.Row + w.Height);
}
