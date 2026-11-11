using System.Globalization;
using Webignite.Core.Documents;

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

/// <summary>
/// What the menu says beside its pages, worked out from the same records Overview reads. Keyed by the page's
/// name, as Core doesn't know the pages themselves.
/// </summary>
public static class MenuFigures
{
    public static IReadOnlyDictionary<string, MenuFigure> For(IReadOnlyList<StoredDocument> documents, DateOnly today, int unreadMail = 0)
    {
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
        if (unreadMail > 0)
            figures["Mail"] = new MenuFigure(string.Create(CultureInfo.InvariantCulture, $"{unreadMail} unread"));
        return figures;
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
    public const int Most = 10;

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
}
