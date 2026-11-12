using System.Globalization;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Renewals;
using Webignite.Core.Retainers;

namespace Webignite.Core.Layout;

/// <summary>How the app's pages are reached: the sidebar, one Menu button, or a dock along the bottom.</summary>
public enum MenuKind
{
    /// <summary>The list down the left, as the app has always had.</summary>
    Sidebar,

    /// <summary>One button; the whole menu opens over the page when it is pressed.</summary>
    Button,

    /// <summary>A bar of the pages used most along the bottom, with the rest behind More.</summary>
    Dock,
}

/// <summary>The few words beside a page in the menu saying how things stand there, and whether something is late.</summary>
public sealed record MenuFigure(string Text, bool Late = false);

/// <summary>Everything the menu's figures are worked out from, read by the app when the menu opens.</summary>
public sealed record MenuFacts
{
    public required DateOnly Today { get; init; }
    public IReadOnlyList<StoredDocument> Documents { get; init; } = [];
    public int UnreadMail { get; init; }
    public IReadOnlyList<Renewal> Renewals { get; init; } = [];
    public IReadOnlyList<Retainer> Retainers { get; init; } = [];

    /// <summary>The retainer invoices already drafted (their keys), so one isn't counted as still to go out.</summary>
    public IReadOnlySet<string> Drafted { get; init; } = new HashSet<string>();

    /// <summary>What came in so far this month.</summary>
    public decimal ReceivedThisMonth { get; init; }

    public IReadOnlyList<Client> Clients { get; init; } = [];
}

/// <summary>
/// What the menu says beside its pages, worked out with the same rules the pages themselves use. Keyed by the
/// page's name, as Core doesn't know the pages themselves. A page with nothing to say has no figure.
/// </summary>
public static class MenuFigures
{
    public static IReadOnlyDictionary<string, MenuFigure> For(IReadOnlyList<StoredDocument> documents, DateOnly today, int unreadMail = 0) =>
        For(new MenuFacts { Today = today, Documents = documents, UnreadMail = unreadMail });

    public static IReadOnlyDictionary<string, MenuFigure> For(MenuFacts facts)
    {
        var today = facts.Today;
        var documents = facts.Documents;
        var figures = new Dictionary<string, MenuFigure>();
        var owed = Receivables.Owed(documents);
        if (owed > 0)
            figures["Invoices"] = new MenuFigure(Whole(owed) + " owed", documents.Any(d => Receivables.IsOverdue(d, today)));
        var out_ = Receivables.QuotesOut(documents, today);
        if (out_ > 0)
            figures["Quotes"] = new MenuFigure(Whole(out_) + " out");
        var waiting = Approvals.Waiting(documents).Count;
        if (waiting > 0)
            figures["Approvals"] = new MenuFigure(string.Create(CultureInfo.InvariantCulture, $"{waiting} waiting"));
        if (facts.UnreadMail > 0)
            figures["Mail"] = new MenuFigure(string.Create(CultureInfo.InvariantCulture, $"{facts.UnreadMail} unread"));
        if (facts.ReceivedThisMonth > 0)
            figures["Money"] = new MenuFigure(Whole(facts.ReceivedThisMonth) + " in");
        if (Renewing(facts.Renewals, today) is { } renewing)
            figures["Renewals"] = renewing;
        if (GoingOut(facts.Retainers, facts.Drafted, today) is { } goingOut)
            figures["Retainers"] = goingOut;
        var building = facts.Clients.Count(Onboarding.InProgress);
        if (building > 0)
            figures["Clients"] = new MenuFigure(building == 1 ? "1 new site" : string.Create(CultureInfo.InvariantCulture, $"{building} new sites"));
        return figures;
    }

    // Past its date comes first, as that is the one that can cost a client their site.
    private static MenuFigure? Renewing(IReadOnlyList<Renewal> renewals, DateOnly today)
    {
        var overdue = renewals.Count(r => RenewalRules.State(r, today) == RenewalState.Overdue);
        if (overdue > 0)
            return new MenuFigure(string.Create(CultureInfo.InvariantCulture, $"{overdue} overdue"), Late: true);
        var soon = renewals.Where(r => RenewalRules.State(r, today) == RenewalState.DueSoon).OrderBy(r => r.RenewsOn).ToList();
        return soon.Count switch
        {
            0 => null,
            1 => new MenuFigure("1 renews " + Wording.Day(soon[0].RenewsOn, today)),
            _ => new MenuFigure(string.Create(CultureInfo.InvariantCulture, $"{soon.Count} renew soon")),
        };
    }

    // The next day retainer invoices are drafted, and how many that day.
    private static MenuFigure? GoingOut(IReadOnlyList<Retainer> retainers, IReadOnlySet<string> drafted, DateOnly today)
    {
        var next = retainers.Select(r => RetainerSchedule.NextDate(r, drafted, today)).Where(d => d is not null).Select(d => d!.Value).ToList();
        if (next.Count == 0)
            return null;
        var first = next.Min();
        var count = next.Count(d => d == first);
        var when = first <= today ? "today" : first.Year == today.Year && first.Month == today.Month ? "on the " + (first.Day == 31 ? "31st" : Wording.DayOfMonth(first.Day)) : Wording.Day(first, today);
        return new MenuFigure(count == 1 ? $"1 goes out {when}" : string.Create(CultureInfo.InvariantCulture, $"{count} go out {when}"));
    }

    // Whole rands, as there is little room: "R 6 825".
    private static string Whole(decimal amount)
    {
        var rand = Money.Rand(Math.Round(amount, 0, MidpointRounding.AwayFromZero));
        var cents = rand.LastIndexOf('.');
        return cents < 0 ? rand : rand[..cents];
    }
}

/// <summary>Which pages sit on the dock, by name, in order. The person adds and takes off their own.</summary>
public static class DockPages
{
    /// <summary>More than this and the dock no longer fits a small window.</summary>
    public const int Most = 12;

    /// <summary>What the dock holds until the person changes it.</summary>
    public static readonly IReadOnlyList<string> Usual = ["Overview", "Approvals", "Mail", "Home", "Clients", "Invoices", "Quotes", "Money", "Files"];

    /// <summary>The dock as saved, without pages the app no longer has, or the usual ones when nothing is saved.</summary>
    public static IReadOnlyList<string> Shown(IReadOnlyList<string>? saved, IReadOnlyCollection<string> known) =>
        [.. (saved ?? Usual).Where(known.Contains).Distinct().Take(Most)];

    public static bool CanAdd(IReadOnlyList<string> dock, string page) => !dock.Contains(page) && dock.Count < Most;

    public static IReadOnlyList<string> Add(IReadOnlyList<string> dock, string page) => CanAdd(dock, page) ? [.. dock, page] : dock;

    /// <summary>The last page can't be taken off: an empty dock would leave only More.</summary>
    public static bool CanRemove(IReadOnlyList<string> dock, string page) => dock.Contains(page) && dock.Count > 1;

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> dock, string page) => CanRemove(dock, page) ? [.. dock.Where(p => p != page)] : dock;

    /// <summary>
    /// Moves one along the dock, to just before or just after another. The dock is left as it is when either isn't
    /// on it, or when one is moved onto itself.
    /// </summary>
    public static IReadOnlyList<string> Move(IReadOnlyList<string> dock, string page, string beside, bool after)
    {
        if (page == beside || !dock.Contains(page) || !dock.Contains(beside))
            return dock;
        var moved = dock.Where(p => p != page).ToList();
        moved.Insert(moved.IndexOf(beside) + (after ? 1 : 0), page);
        return moved;
    }
}

/// <summary>
/// What a name on the dock stands for. A page goes by its own name ("Mail"), as it always has; a tool and a whole
/// group of the menu carry what they are in front, so a saved dock from before still reads the same.
/// </summary>
public static class DockKeys
{
    private const string ToolMark = "tool:";
    private const string GroupMark = "group:";

    public static string Tool(string kind) => ToolMark + kind;

    public static string Group(string key) => GroupMark + key;

    /// <summary>The tool's kind, when the name is a tool's.</summary>
    public static string? AsTool(string key) => key.StartsWith(ToolMark, StringComparison.Ordinal) ? key[ToolMark.Length..] : null;

    /// <summary>The group's key, when the name is a group's. A group on the dock opens a short list of what is in it.</summary>
    public static string? AsGroup(string key) => key.StartsWith(GroupMark, StringComparison.Ordinal) ? key[GroupMark.Length..] : null;
}

/// <summary>The small things that can sit beside the dock, in the space left and right of it.</summary>
public enum BarWidget { Today, Clock, Timer, Chat, Search, QuickNote }

/// <summary>Which side of the dock.</summary>
public enum BarSide { Left, Right }

/// <summary>What sits left and right of the dock, in order.</summary>
public sealed record BarLayout(IReadOnlyList<BarWidget> Left, IReadOnlyList<BarWidget> Right)
{
    public IEnumerable<BarWidget> All => Left.Concat(Right);
}

/// <summary>What sits beside the dock. The person adds, moves and takes off their own.</summary>
public static class DockBar
{
    /// <summary>What a new install shows: the day and the timer on the left, chat and the time on the right.</summary>
    public static readonly BarLayout Usual = new([BarWidget.Today, BarWidget.Timer], [BarWidget.Chat, BarWidget.Clock]);

    /// <summary>What each is called where the person picks them.</summary>
    public static string Name(BarWidget widget) => widget switch
    {
        BarWidget.Today => "Today's date",
        BarWidget.Clock => "Clock",
        BarWidget.Timer => "Timer",
        BarWidget.Chat => "Chat",
        BarWidget.Search => "Search",
        BarWidget.QuickNote => "Quick note",
        _ => throw new ArgumentOutOfRangeException(nameof(widget), widget, null),
    };

    /// <summary>
    /// The bar as saved, or the usual one when nothing is saved. Names this version doesn't know are left out, and
    /// one saved on both sides stays only where it comes first. Both sides empty is allowed: that is a bare dock.
    /// </summary>
    public static BarLayout Shown(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null && right is null)
            return Usual;
        var seen = new HashSet<BarWidget>();
        List<BarWidget> Read(IReadOnlyList<string>? saved) =>
            [.. (saved ?? []).Select(name => Enum.TryParse<BarWidget>(name, out var widget) && Enum.IsDefined(widget) ? widget : (BarWidget?)null)
                .Where(widget => widget is not null && seen.Add(widget.Value)).Select(widget => widget!.Value)];
        var onLeft = Read(left);
        return new(onLeft, Read(right));
    }

    /// <summary>What isn't on the bar yet, in the order of <see cref="BarWidget"/>.</summary>
    public static IReadOnlyList<BarWidget> Available(BarLayout bar) => [.. Enum.GetValues<BarWidget>().Except(bar.All)];

    /// <summary>Adds one at the end of a side. One already on the bar moves to that side.</summary>
    public static BarLayout Add(BarLayout bar, BarWidget widget, BarSide side)
    {
        var without = Remove(bar, widget);
        return side == BarSide.Left ? without with { Left = [.. without.Left, widget] } : without with { Right = [.. without.Right, widget] };
    }

    public static BarLayout Remove(BarLayout bar, BarWidget widget) =>
        new([.. bar.Left.Where(w => w != widget)], [.. bar.Right.Where(w => w != widget)]);

    /// <summary>Moves one to the other side of the dock, or leaves the bar as it is when it isn't on it.</summary>
    public static BarLayout Across(BarLayout bar, BarWidget widget) =>
        bar.Left.Contains(widget) ? Add(bar, widget, BarSide.Right) : bar.Right.Contains(widget) ? Add(bar, widget, BarSide.Left) : bar;

    /// <summary>The names to save for a side.</summary>
    public static IReadOnlyList<string> Saved(IReadOnlyList<BarWidget> side) => [.. side.Select(w => w.ToString())];
}
