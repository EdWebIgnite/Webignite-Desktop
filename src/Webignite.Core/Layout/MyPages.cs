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
    Files,
    ClientMail,
}

/// <summary>How a widget wears its colour. None is a solid fill: a page of solid blocks is as tiring as a page of white ones.</summary>
public enum WidgetTint
{
    /// <summary>The whole widget in a faint wash of the colour, its heading a little deeper.</summary>
    Wash,

    /// <summary>A plain widget with only its heading in the colour.</summary>
    Heading,

    /// <summary>A plain widget with a line of the colour along its top.</summary>
    Line,
}

/// <summary>
/// A widget's place on a page's grid: <see cref="MyPages.Columns"/> narrow columns across, short rows going down
/// as far as needed. <see cref="Target"/> is the project or client it shows, or the Files widget's folder;
/// <see cref="Text"/> is a note's words.
/// </summary>
public sealed record Widget
{
    public required string Id { get; init; }
    public WidgetKind Kind { get; init; }
    public int Column { get; init; }
    public int Row { get; init; }
    public int Width { get; init; } = 16;
    public int Height { get; init; } = 16;
    public string? Target { get; init; }
    public string? Text { get; init; }

    /// <summary>One of <see cref="WidgetLooks.Colours"/>, or null for a plain widget.</summary>
    public string? Colour { get; init; }

    public WidgetTint Tint { get; init; }
}

/// <summary>A page the person made: a name in the sidebar and the widgets on it.</summary>
public sealed record MyPage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<Widget> Widgets { get; init; } = [];

    /// <summary>
    /// How many columns its widgets were placed on. Pages saved before the finer grid have 0 here (twelve columns)
    /// and are scaled up when read, so they look the same.
    /// </summary>
    public int Grid { get; init; }
}

/// <summary>
/// The rules for the person's own pages. Widgets sit where they're put, gaps and all, on a fine grid of
/// <see cref="Columns"/> columns that keeps their edges lined up. One dropped or grown onto others nudges them
/// down out of its way, so widgets never overlap.
/// </summary>
public static class MyPages
{
    public const int Columns = 48;

    /// <summary>How many fine columns (and rows) make one of the old grid's: pages saved on twelve columns are scaled by this.</summary>
    public const int Fine = Columns / 12;

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
        WidgetKind.Files => "Files",
        WidgetKind.ClientMail => "A client's mail",
        _ => "Colours",
    };

    /// <summary>Widgets that show one project or one client, picked when added.</summary>
    public static bool NeedsTarget(WidgetKind kind) => kind is WidgetKind.Project or WidgetKind.Client or WidgetKind.ClientMail;

    /// <summary>The size a widget starts at, in fine columns and rows.</summary>
    public static (int Width, int Height) StartSize(WidgetKind kind)
    {
        var (width, height) = kind switch
        {
            WidgetKind.Project or WidgetKind.Month or WidgetKind.Files => (6, 5),
            WidgetKind.NeedsYou or WidgetKind.Mail or WidgetKind.ClientMail => (6, 4),
            WidgetKind.Calculator => (3, 6),
            WidgetKind.Timer => (3, 2),
            WidgetKind.Note => (3, 3),
            _ => (4, 4),
        };
        return (width * Fine, height * Fine);
    }

    /// <summary>The smallest a widget can be and still show something useful, in fine columns and rows.</summary>
    public static (int Width, int Height) LeastSize(WidgetKind kind)
    {
        var (width, height) = kind switch
        {
            WidgetKind.Calculator => (3, 5),
            WidgetKind.Month => (4, 4),
            WidgetKind.Files => (3, 3),
            _ => (2, 2),
        };
        return (width * Fine, height * Fine);
    }

    public static int MostHeight => 16 * Fine;

    public static MyPage NewPage(IEnumerable<MyPage> existing)
    {
        var names = existing.Select(p => p.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var name = "My page";
        for (var n = 2; names.Contains(name); n++)
            name = $"My page {n}";
        return new MyPage { Id = NewId(), Name = name, Grid = Columns };
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

    /// <summary>Takes a widget off; the others stay where they are.</summary>
    public static MyPage Remove(MyPage page, string id) =>
        page with { Widgets = [.. page.Widgets.Where(w => w.Id != id)] };

    /// <summary>Puts the Files widget on another folder.</summary>
    /// <summary>A widget's colour and how it wears it. A colour that isn't on the list, or none, makes it plain again.</summary>
    public static MyPage SetLook(MyPage page, string id, string? colour, WidgetTint tint) => page with
    {
        Widgets = [.. page.Widgets.Select(w => w.Id == id ? w with { Colour = WidgetLooks.Colours.Contains(colour ?? "") ? colour : null, Tint = tint } : w)],
    };

    public static MyPage SetTarget(MyPage page, string id, string target) => page with
    {
        Widgets = [.. page.Widgets.Select(w => w.Id == id ? w with { Target = target } : w)],
    };

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
    /// A saved page made sound: a page from the twelve-column grid scaled up to the fine one, sizes and places kept
    /// on the grid, and widgets that overlap moved down, so a page saved by an older version (or edited by hand)
    /// still lays out. Gaps are left as they are.
    /// </summary>
    public static MyPage Reconcile(MyPage page)
    {
        var scale = page.Grid == Columns ? 1 : Fine;
        return page with
        {
            Name = string.IsNullOrWhiteSpace(page.Name) ? "My page" : page.Name,
            Grid = Columns,
            Widgets = Settle([.. page.Widgets.Select(w => Fit(scale == 1 ? w : w with
            {
                Column = w.Column * scale, Row = w.Row * scale, Width = w.Width * scale, Height = w.Height * scale,
            }))], null),
        };
    }

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
    /// overlap, and otherwise stay put. The order of the list is kept.
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
        var byId = placed.ToDictionary(w => w.Id);
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

    /// <summary>
    /// The folder a Files widget shows: the one it was left on while it's still there, else the client folders,
    /// else Documents.
    /// </summary>
    public static string StartFolder(string? saved, Func<string, bool> exists, string? clientRoot, string documents) =>
        saved is { Length: > 0 } && exists(saved) ? saved : clientRoot is { Length: > 0 } && exists(clientRoot) ? clientRoot : documents;

    /// <summary>How many rows the page takes, for the height of its grid.</summary>
    public static int Rows(MyPage page) => page.Widgets.Count == 0 ? 0 : page.Widgets.Max(w => w.Row + w.Height);
}

/// <summary>
/// Which of Overview's own parts show. The person can take any of them off and put widgets of their own under
/// what is left, so the page they start the day on is theirs to arrange.
/// </summary>
public sealed record OverviewParts
{
    /// <summary>The four figures along the top: came in, owed, overdue, quotes out.</summary>
    public bool Figures { get; init; } = true;
    public bool NeedsYou { get; init; } = true;
    public bool Today { get; init; } = true;

    /// <summary>The name the page of the person's own widgets on Overview is saved under.</summary>
    public const string BoardId = "overview";

    /// <summary>Overview's own widgets before any have been added: an empty page.</summary>
    public static MyPage EmptyBoard() => new() { Id = BoardId, Name = "Overview", Grid = MyPages.Columns };
}

/// <summary>How much of its colour each part of a widget takes, from none (0) to all of it (1), mixed into the page's own surface.</summary>
/// <param name="Body">The widget's background.</param>
/// <param name="Head">The band behind its heading.</param>
/// <param name="Edge">Its outline.</param>
/// <param name="Line">The line along its top.</param>
public sealed record WidgetShade(double Body, double Head, double Edge, double Line);

/// <summary>
/// Colour on the person's own widgets. The colours are the sixteen used for mail categories and file tags, but a
/// widget never takes one at full strength: the colour is mixed into the surface in small amounts, so widgets read
/// as soft pastels on a light look and as deep tints on a dark one, and the writing on them keeps its contrast.
/// </summary>
public static class WidgetLooks
{
    public static IReadOnlyList<string> Colours => Webignite.Core.Mail.MailCategories.Colours;

    public static string Label(WidgetTint tint) => tint switch
    {
        WidgetTint.Wash => "Soft wash",
        WidgetTint.Heading => "Heading only",
        _ => "Top line",
    };

    public static WidgetShade Shade(WidgetTint tint) => tint switch
    {
        WidgetTint.Wash => new(0.07, 0.15, 0.30, 0),
        WidgetTint.Heading => new(0, 0.18, 0.24, 0),
        _ => new(0, 0, 0, 1),
    };

    /// <summary>One colour channel (0 to 255) of a part: the surface's, moved towards the colour's by the share given.</summary>
    public static byte Mix(byte surface, byte colour, double share) =>
        (byte)Math.Round(surface + (colour - surface) * Math.Clamp(share, 0, 1));
}
