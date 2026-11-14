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

/// <summary>How a widget coloured before its parts could be picked apart wore its one colour. Kept only to read those widgets (<see cref="WidgetLooks.Of"/>).</summary>
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

    /// <summary>A note with its formatting (bold, lists, checklists, links), as HTML; null for a note typed before notes could be formatted, which is read from <see cref="Text"/>.</summary>
    public string? Html { get; init; }

    /// <summary>One of <see cref="WidgetLooks.Colours"/>, or null for a plain widget.</summary>
    public string? Colour { get; init; }

    public WidgetTint Tint { get; init; }

    /// <summary>
    /// How strongly the colour shows, 10 to 100 (percent). 0 is a widget coloured before there was a choice: one of
    /// the bright category colours as a faint wash.
    /// </summary>
    public int Strength { get; init; }

    /// <summary>
    /// Its heading, body and top line colours, each picked on its own. Null for a plain widget, and for one
    /// coloured before the parts could be picked apart, which <see cref="WidgetLooks.Of"/> reads from the three
    /// fields above.
    /// </summary>
    public TileLook? Look { get; init; }
}

/// <summary>What a group of pages in the sidebar is gathered under.</summary>
public enum PageGroupKind { None, Client, Project }

/// <summary>Pages listed together in the sidebar: those for one client, for one project, or (first, with no name) for neither.</summary>
public sealed record PageGroup(PageGroupKind Kind, string? Id, string Name, IReadOnlyList<MyPage> Pages);

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

    /// <summary>The client this page is for, by id; null for a page that is for no client.</summary>
    public string? ClientId { get; init; }

    /// <summary>The project this page is for, by id; null for none. A page can be for a client, a project, both or neither.</summary>
    public string? ProjectId { get; init; }
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
    /// <summary>Says which client and which project a page is for; null for either is "none".</summary>
    public static MyPage For(MyPage page, string? clientId, string? projectId) => page with
    {
        ClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId,
        ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
    };

    /// <summary>
    /// The pages as the sidebar lists them: first those for no client or project, in the order they were made, then
    /// a group for each client that has pages, by name, then one for each project that has pages for no client. A
    /// page for a client and a project is under its client. A page whose client or project is gone is back among
    /// the ungrouped ones. The names come from <paramref name="clientName"/> and <paramref name="projectName"/>,
    /// which give null for one that is gone.
    /// </summary>
    public static IReadOnlyList<PageGroup> Grouped(IEnumerable<MyPage> pages, Func<string, string?> clientName, Func<string, string?> projectName)
    {
        var loose = new List<MyPage>();
        var clients = new Dictionary<string, (string Name, List<MyPage> Pages)>(StringComparer.Ordinal);
        var projects = new Dictionary<string, (string Name, List<MyPage> Pages)>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            if (page.ClientId is { } client && clientName(client) is { } named)
                Into(clients, client, named, page);
            else if (page.ProjectId is { } project && projectName(project) is { } called)
                Into(projects, project, called, page);
            else
                loose.Add(page);
        }
        var groups = new List<PageGroup> { new(PageGroupKind.None, null, "", loose) };
        groups.AddRange(clients.OrderBy(g => g.Value.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => new PageGroup(PageGroupKind.Client, g.Key, g.Value.Name, g.Value.Pages)));
        groups.AddRange(projects.OrderBy(g => g.Value.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => new PageGroup(PageGroupKind.Project, g.Key, g.Value.Name, g.Value.Pages)));
        return groups;

        static void Into(Dictionary<string, (string Name, List<MyPage> Pages)> groups, string id, string name, MyPage page)
        {
            if (!groups.TryGetValue(id, out var group))
                groups[id] = group = (name, []);
            group.Pages.Add(page);
        }
    }

    /// <summary>"For Bosveld Builders · New website", "For Bosveld Builders", "For New website", or nothing for a page for neither.</summary>
    public static string ForText(string? clientName, string? projectName) => (clientName, projectName) switch
    {
        ({ Length: > 0 }, { Length: > 0 }) => $"For {clientName} · {projectName}",
        ({ Length: > 0 }, _) => $"For {clientName}",
        (_, { Length: > 0 }) => $"For {projectName}",
        _ => "",
    };

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
    /// <summary>A widget's colours and their intensity, kept in the one place; a plain look clears them.</summary>
    public static MyPage SetLook(MyPage page, string id, TileLook? look) => page with
    {
        Widgets = [.. page.Widgets.Select(w => w.Id == id
            ? w with { Look = TileLook.Tidied(look), Colour = null, Tint = WidgetTint.Wash, Strength = 0 }
            : w)],
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

    /// <summary>
    /// Keeps a note: its words, for searching and for showing where formatting can't be, and the same with its
    /// formatting as HTML.
    /// </summary>
    public static MyPage SetNote(MyPage page, string id, string text, string html) => page with
    {
        Widgets = [.. page.Widgets.Select(w => w.Id == id ? w with { Text = text, Html = html } : w)],
    };

    /// <summary>A note as the editor works on it: its formatted form when it has one, else its plain words.</summary>
    public static Webignite.Core.Writing.RichDocument NoteOf(Widget widget) =>
        string.IsNullOrWhiteSpace(widget.Html)
            ? Webignite.Core.Writing.RichText.FromText(widget.Text)
            : Webignite.Core.Writing.RichHtml.Read(widget.Html, Webignite.Core.Writing.RichHtml.DataAddress);

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

/// <summary>
/// The colours of a tile: a widget on the person's own pages, or any card in the app. The band behind its heading,
/// its body and a line along its top are each picked on their own, or left plain (null). A colour is a code such
/// as #C6E3F7, or one of the category colours by name.
/// </summary>
public sealed record TileLook
{
    public string? Head { get; init; }
    public string? Body { get; init; }
    public string? Line { get; init; }

    /// <summary>How strongly the heading and body colours show, 10 to 100 (percent).</summary>
    public int Strength { get; init; } = WidgetLooks.UsualStrength;

    public bool IsPlain => Head is null && Body is null && Line is null;

    /// <summary>A look with only colours that can be kept and an intensity on the scale; null when nothing is coloured.</summary>
    public static TileLook? Tidied(TileLook? look)
    {
        if (look is null)
            return null;
        var tidy = new TileLook
        {
            Head = WidgetLooks.Known(look.Head), Body = WidgetLooks.Known(look.Body), Line = WidgetLooks.Known(look.Line),
            Strength = WidgetLooks.Strength(look.Strength),
        };
        return tidy.IsPlain ? null : tidy;
    }
}

/// <summary>A pastel offered for widgets: its name and colour code.</summary>
public sealed record Pastel(string Name, string Hex);

/// <summary>
/// Colour on the person's own widgets. A widget takes one of the pastels offered, or any colour by its code, and
/// wears it as a wash, on its heading only, or as a line along the top, as strongly as its intensity says. The
/// colour is mixed into the look's own surface, so the same pastel is soft on a light look and a deep tint on a
/// dark one; and however strong it is set, it is held back to where the writing on it can still be read.
/// </summary>
public static class WidgetLooks
{
    /// <summary>The bright category colours by name, which widgets coloured earlier may still have.</summary>
    public static IReadOnlyList<string> Colours => Webignite.Core.Mail.MailCategories.Colours;

    /// <summary>
    /// The pastels offered: light, low in strength of colour, and spread round the colour wheel, with three quiet
    /// neutrals at the end. These are the colours a person picks for their own widgets, so they sit here with the
    /// category colours' names and not in the palettes, which hold the app's own colours.
    /// </summary>
    public static IReadOnlyList<Pastel> Pastels { get; } =
    [
        new("Blush", "#F9D5DC"), new("Rose", "#F7C6D0"), new("Coral", "#FBCFC0"), new("Peach", "#FDDDBE"),
        new("Apricot", "#FCE5B8"), new("Butter", "#FBF1B5"), new("Pistachio", "#E1EFC2"), new("Sage", "#CFE3CC"),
        new("Mint", "#C5ECDB"), new("Aqua", "#C2EBEB"), new("Sky", "#C6E3F7"), new("Powder blue", "#CBD8F7"),
        new("Periwinkle", "#D3D1F8"), new("Lavender", "#E2D2F5"), new("Lilac", "#EFD0EE"), new("Orchid", "#F5CDE3"),
        new("Sand", "#EBDFCF"), new("Stone", "#DFDCD5"), new("Mist", "#D9E1E7"), new("Slate", "#CFD5DE"),
    ];

    /// <summary>The intensity a newly coloured widget starts at, the least and the most, and the step between.</summary>
    public const int UsualStrength = 70;
    public const int LeastStrength = 10;
    public const int MostStrength = 100;
    public const int StrengthStep = 10;

    /// <summary>The least the writing may stand out from a widget's colour (the usual rule for body text).</summary>
    public const double LeastContrast = 4.5;

    /// <summary>A colour as it is kept: a category colour's name, or a code as #RRGGBB. Null for anything else.</summary>
    public static string? Known(string? colour)
    {
        var text = (colour ?? "").Trim();
        if (Colours.Contains(text))
            return text;
        // Only codes count here: "red" or "rgb(…)" typed in the box would be a guess at what was meant.
        var code = text.TrimStart('#');
        return code.Length is 3 or 6 && code.All(Uri.IsHexDigit) && Webignite.Core.Tools.Colours.Parse("#" + code) is { } rgb ? rgb.Hex : null;
    }

    /// <summary>The colour of a kept code; null for a name, which the palette of the look on screen gives.</summary>
    public static Webignite.Core.Tools.Rgb? Code(string? colour) =>
        colour is { Length: > 0 } && colour[0] == '#' ? Webignite.Core.Tools.Colours.Parse(colour) : null;

    /// <summary>An intensity brought onto the scale: 10 to 100 in tens.</summary>
    public static int Strength(int typed) =>
        Math.Clamp((int)Math.Round(typed / (double)StrengthStep) * StrengthStep, LeastStrength, MostStrength);

    /// <summary>The share of a colour a heading or body takes at an intensity: 70 is seven tenths of the way from the surface to the colour.</summary>
    public static double Share(int strength) => Strength(strength) / 100.0;

    /// <summary>
    /// A widget's look. One coloured before its parts could be picked apart had a single colour and a way of
    /// wearing it, which is read as the parts it coloured: a wash was heading and body, and so on.
    /// </summary>
    public static TileLook? Of(Widget widget)
    {
        if (widget.Look is not null)
            return TileLook.Tidied(widget.Look);
        if (Known(widget.Colour) is not { } colour)
            return null;
        // The first coloured widgets were a faint wash of a bright colour: the lowest intensities keep them quiet.
        var strength = widget.Strength > 0 ? widget.Strength : widget.Tint == WidgetTint.Wash ? LeastStrength : 2 * LeastStrength;
        return widget.Tint switch
        {
            WidgetTint.Wash => new TileLook { Head = colour, Body = colour, Strength = strength },
            WidgetTint.Heading => new TileLook { Head = colour, Strength = strength },
            _ => new TileLook { Line = colour, Strength = strength },
        };
    }

    /// <summary>One colour channel (0 to 255) of a part: the surface's, moved towards the colour's by the share given.</summary>
    public static byte Mix(byte surface, byte colour, double share) =>
        (byte)Math.Round(surface + (colour - surface) * Math.Clamp(share, 0, 1));

    public static Webignite.Core.Tools.Rgb Mix(Webignite.Core.Tools.Rgb surface, Webignite.Core.Tools.Rgb colour, double share) =>
        new(Mix(surface.R, colour.R, share), Mix(surface.G, colour.G, share), Mix(surface.B, colour.B, share));

    /// <summary>
    /// The share of a colour a part may take and still be read on: the share asked for, or the most under it at
    /// which the writing keeps its contrast. A dark colour typed in and set to full is held back to a tint of
    /// itself, so no choice makes a widget unreadable.
    /// </summary>
    public static double Readable(Webignite.Core.Tools.Rgb surface, Webignite.Core.Tools.Rgb colour, Webignite.Core.Tools.Rgb writing, double share)
    {
        for (var tried = Math.Clamp(share, 0, 1); tried > 0.02; tried -= 0.04)
            if (Webignite.Core.Tools.Colours.Contrast(Mix(surface, colour, tried), writing) >= LeastContrast)
                return tried;
        return 0.02;
    }
}
