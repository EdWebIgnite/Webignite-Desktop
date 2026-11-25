namespace Webignite.Desktop.ViewModels;

/// <summary>The tabs on the Tools page.</summary>
public enum ToolTab
{
    Computer,
    Web,
    Images,
}

/// <summary>
/// One tool on the Tools page: a small tool in its own window (<see cref="Kind"/>) or a whole page that is a tool
/// (<see cref="Page"/>, such as PDF tools), with its name, icon, the tab it sits under and a line on what it does.
/// </summary>
public sealed record ToolEntry(string Title, string Icon, ToolTab Tab, string About, ToolKind? Kind = null, Page? Page = null)
{
    /// <summary>Its key on the menus, the same one the sidebar knows it by, so pinning it there finds it.</summary>
    public string MenuKey => Kind is { } kind ? SidebarViewModel.KeyOf(kind) : SidebarViewModel.KeyOf(Page!.Value);
}

/// <summary>
/// Every tool, in the order the Tools page shows them under each tab. The sidebar takes its tool entries from here
/// too, so a tool is named once. Files, Notes and the business pages aren't tools: they stay on the menus.
/// </summary>
public static class ToolCatalogue
{
    public static IReadOnlyList<ToolEntry> All { get; } =
    [
        // On this computer
        new("PDF tools", "Quotes", ToolTab.Computer, "Join, split, reorder and shrink PDF files.", Page: Page.Pdf),
        new("Compare", "Compare", ToolTab.Computer, "See what differs between two files or two pieces of text.", ToolKind.Compare),
        new("Duplicate files", "Duplicates", ToolTab.Computer, "Find copies of the same file in a folder.", ToolKind.Duplicates),
        new("Calculator", "Calculator", ToolTab.Computer, "Quick sums without leaving Webignite.", ToolKind.Calculator),
        new("Password generator", "Key", ToolTab.Computer, "Word or random passwords, made on this PC and never saved.", ToolKind.Passwords),
        new("Text snippets", "Snippets", ToolTab.Computer, "Bits of text you type often, ready to paste.", ToolKind.Snippets),
        new("Text from the screen", "ScreenText", ToolTab.Computer, "Copy the words out of a picture or any part of the screen.", ToolKind.ScreenText),
        new("Screen ruler", "Ruler", ToolTab.Computer, "Measure anything on screen in pixels.", ToolKind.Ruler),
        // For websites
        new("Site check", "SiteCheck", ToolTab.Web, "Check that client websites are up and their certificates are in date.", Page: Page.SiteCheck),
        new("Site crawler", "Crawler", ToolTab.Web, "Go through a whole site for broken links and missing titles.", Page: Page.Crawler),
        new("Mark-up", "Markup", ToolTab.Web, "Draw and write on a screenshot to show a client what you mean.", Page: Page.Markup),
        new("Browser and phone preview", "Browser", ToolTab.Web, "See a page at phone, tablet and desktop sizes.", ToolKind.Browser),
        new("Page to PDF or picture", "PageCapture", ToolTab.Web, "Save a whole web page as a PDF or a picture.", ToolKind.PageCapture),
        new("DNS records", "Dns", ToolTab.Web, "Look up where a domain points and who handles its mail.", ToolKind.Dns),
        new("Web helpers", "Code", ToolTab.Web, "Placeholder pictures, px to rem and other small helpers.", ToolKind.WebHelpers),
        new("QR code", "Qr", ToolTab.Web, "A QR code for a link, a Wi-Fi network or a phone number.", ToolKind.Qr),
        // Pictures and brand
        new("Website images", "Picture", ToolTab.Images, "Shrink and convert pictures so pages load fast.", ToolKind.Images),
        new("Logos and brand sheet", "Logos", ToolTab.Images, "A logo pack, favicons and a brand sheet from one file.", ToolKind.Logos),
        new("Social images", "Social", ToolTab.Images, "Pictures cut to the right size for each social network.", ToolKind.Social),
        new("Colours", "Palette", ToolTab.Images, "Pick a colour from the screen and get its codes, shades and what goes with it.", ToolKind.Colours),
    ];

    public static string TabTitle(ToolTab tab) => tab switch
    {
        ToolTab.Computer => "Computer",
        ToolTab.Web => "Web",
        _ => "Pictures and brand",
    };

    public static ToolEntry Of(ToolKind kind) => All.First(t => t.Kind == kind);

    /// <summary>A small tool's entry for the sidebar, named as on the Tools page.</summary>
    public static ToolLink Link(ToolKind kind)
    {
        var entry = Of(kind);
        return new ToolLink(kind, entry.Title, entry.Icon);
    }

    /// <summary>The menu keys of everything that lives on the Tools page, so the menus start without them.</summary>
    public static IReadOnlyList<string> MenuKeys { get; } = [.. All.Select(t => t.MenuKey)];
}
