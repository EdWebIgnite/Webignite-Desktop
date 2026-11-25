namespace Webignite.Desktop.ViewModels;

/// <summary>The four areas of the app, as the tabs across the top of the window.</summary>
public enum AppArea
{
    Mail,
    Business,
    Computer,
    Tools,
}

/// <summary>
/// One page or tool as its area's page shows it: a small tool in its own window (<see cref="Kind"/>), a page
/// (<see cref="Page"/>) or Planner with its own sidebar (<see cref="Section"/>), with its name, icon, the heading it
/// sits under there and a line on what it does. <see cref="Starts"/> says whether it is on the sidebar to begin with.
/// </summary>
public sealed record AppEntry(string Title, string Icon, AppArea Area, string Heading, string About, ToolKind? Kind = null, Page? Page = null,
    string? Section = null, bool Starts = false)
{
    /// <summary>Its key on the menus, the same one the sidebar knows it by, so pinning it finds it.</summary>
    public string MenuKey => Kind is { } kind ? SidebarViewModel.KeyOf(kind)
        : Section is { } section ? SidebarViewModel.KeyOfSection(section)
        : SidebarViewModel.KeyOf(Page!.Value);
}

/// <summary>
/// Everything the app has, by area, in the order each area's page shows it. The sidebar is the shortlist: it starts
/// with the few marked <see cref="AppEntry.Starts"/> and holds whatever the person pins. Pages and tools are named
/// here once; the sidebar takes its names from here too.
/// </summary>
public static class ToolCatalogue
{
    private const string Day = "Day to day";
    private const string Clients = "Clients";
    private const string Billing = "Billing";
    private const string Kept = "Files and notes";
    private const string Small = "Small tools";
    private const string Sites = "Websites";
    private const string Brand = "Pictures and brand";

    public static IReadOnlyList<AppEntry> All { get; } =
    [
        new("Mail", "Mail", AppArea.Mail, "Mail", "Every mailbox in one place.", Page: Page.Mail, Starts: true),

        new("Overview", "Overview", AppArea.Business, Day, "What needs you today: money in, what's owed and what's late.", Page: Page.Overview, Starts: true),
        new("Planner", "Planner", AppArea.Business, Day, "Projects, tasks, your calendar, diary and time.", Section: "planner", Starts: true),
        new("All my pages", "Tiles", AppArea.Business, Day, "Pages you put together yourself from widgets.", Page: Page.MyPages),
        new("Clients", "Clients", AppArea.Business, Clients, "Each client with their quotes, invoices, mail, files and logins.", Page: Page.Clients, Starts: true),
        new("Contacts", "Contacts", AppArea.Business, Clients, "The people you deal with, linked to their clients.", Page: Page.Contacts),
        new("Recurring", "Retainers", AppArea.Business, Clients, "Retainers, hosting and domains that bill on a schedule.", Page: Page.Retainers),
        new("Quotes", "Quotes", AppArea.Business, Billing, "Write a quote, send it and see what's still out.", Page: Page.Quotes, Starts: true),
        new("Invoices", "Invoices", AppArea.Business, Billing, "Invoice, send through your own mail and see what's owed.", Page: Page.Invoices, Starts: true),
        new("Money", "Money", AppArea.Business, Billing, "What came in, what's expected and the bills to pay.", Page: Page.Money),
        new("Studio", "Studio", AppArea.Business, Billing, "How the work links up, from first quote to paid.", Page: Page.Studio),

        new("Files", "Folder", AppArea.Computer, Kept, "Your folders and client files, with tags and quick look.", Page: Page.Files, Starts: true),
        new("Notes", "Notes", AppArea.Computer, Kept, "Notebooks, checklists and notes about clients and projects.", Page: Page.Notes, Starts: true),
        new("PDF tools", "Quotes", AppArea.Computer, Kept, "Join, split, reorder and shrink PDF files.", Page: Page.Pdf),
        new("Compare", "Compare", AppArea.Computer, Small, "See what differs between two files or two pieces of text.", ToolKind.Compare),
        new("Duplicate files", "Duplicates", AppArea.Computer, Small, "Find copies of the same file in a folder.", ToolKind.Duplicates),
        new("Calculator", "Calculator", AppArea.Computer, Small, "Quick sums without leaving Webignite.", ToolKind.Calculator),
        new("Password generator", "Key", AppArea.Computer, Small, "Word or random passwords, made on this PC and never saved.", ToolKind.Passwords),
        new("Text snippets", "Snippets", AppArea.Computer, Small, "Bits of text you type often, ready to paste.", ToolKind.Snippets),
        new("Text from the screen", "ScreenText", AppArea.Computer, Small, "Copy the words out of a picture or any part of the screen.", ToolKind.ScreenText),
        new("Screen ruler", "Ruler", AppArea.Computer, Small, "Measure anything on screen in pixels.", ToolKind.Ruler),

        new("Site check", "SiteCheck", AppArea.Tools, Sites, "Check that client websites are up and their certificates are in date.", Page: Page.SiteCheck),
        new("Site crawler", "Crawler", AppArea.Tools, Sites, "Go through a whole site for broken links and missing titles.", Page: Page.Crawler),
        new("Mark-up", "Markup", AppArea.Tools, Sites, "Draw and write on a screenshot to show a client what you mean.", Page: Page.Markup),
        new("Browser and phone preview", "Browser", AppArea.Tools, Sites, "See a page at phone, tablet and desktop sizes.", ToolKind.Browser),
        new("Page to PDF or picture", "PageCapture", AppArea.Tools, Sites, "Save a whole web page as a PDF or a picture.", ToolKind.PageCapture),
        new("DNS records", "Dns", AppArea.Tools, Sites, "Look up where a domain points and who handles its mail.", ToolKind.Dns),
        new("Web helpers", "Code", AppArea.Tools, Sites, "Placeholder pictures, px to rem and other small helpers.", ToolKind.WebHelpers),
        new("QR code", "Qr", AppArea.Tools, Sites, "A QR code for a link, a Wi-Fi network or a phone number.", ToolKind.Qr),
        new("Website images", "Picture", AppArea.Tools, Brand, "Shrink and convert pictures so pages load fast.", ToolKind.Images),
        new("Logos and brand sheet", "Logos", AppArea.Tools, Brand, "A logo pack, favicons and a brand sheet from one file.", ToolKind.Logos),
        new("Social images", "Social", AppArea.Tools, Brand, "Pictures cut to the right size for each social network.", ToolKind.Social),
        new("Colours", "Palette", AppArea.Tools, Brand, "Pick a colour from the screen and get its codes, shades and what goes with it.", ToolKind.Colours),
    ];

    public static string AreaTitle(AppArea area) => area switch
    {
        AppArea.Mail => "Mail",
        AppArea.Business => "Business",
        AppArea.Computer => "Computer",
        _ => "Tools",
    };

    public static string AreaIcon(AppArea area) => area switch
    {
        AppArea.Mail => "Mail",
        AppArea.Business => "Overview",
        AppArea.Computer => "Folder",
        _ => "Tools",
    };

    /// <summary>The page a tab opens: Mail itself, or the area's page of tiles.</summary>
    public static Page AreaPage(AppArea area) => area switch
    {
        AppArea.Mail => Page.Mail,
        AppArea.Business => Page.Business,
        AppArea.Computer => Page.Computer,
        _ => Page.Tools,
    };

    /// <summary>
    /// The area a page belongs to, so its tab is lit while the page is open: its own area, an area's page of tiles,
    /// or Business for the Planner pages. Settings belongs to none.
    /// </summary>
    public static AppArea? AreaOf(Page page, bool inPlanner)
    {
        if (All.FirstOrDefault(e => e.Page == page) is { } entry)
            return entry.Area;
        return page switch
        {
            Page.Business => AppArea.Business,
            Page.Computer => AppArea.Computer,
            Page.Tools => AppArea.Tools,
            _ => inPlanner ? AppArea.Business : null,
        };
    }

    public static AppEntry Of(ToolKind kind) => All.First(t => t.Kind == kind);

    public static AppEntry Of(Page page) => All.First(t => t.Page == page);

    /// <summary>A small tool's entry for the sidebar, named as on its area's page.</summary>
    public static ToolLink Link(ToolKind kind)
    {
        var entry = Of(kind);
        return new ToolLink(kind, entry.Title, entry.Icon);
    }

    /// <summary>
    /// The menu keys of what starts off the sidebar: everything not marked to start there, and the areas' own pages,
    /// which the tabs open.
    /// </summary>
    public static IReadOnlyList<string> OffToStart { get; } =
    [
        .. All.Where(t => !t.Starts).Select(t => t.MenuKey),
        SidebarViewModel.KeyOf(Page.Business), SidebarViewModel.KeyOf(Page.Computer), SidebarViewModel.KeyOf(Page.Tools),
    ];
}
