using Webignite.Core.Backups;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Layout;
using Webignite.Core.Mail;
using Webignite.Core.Projects;
using Webignite.Core.Tasks;
using Webignite.Core.Web;
using Webignite.Core.Writing;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>Saved Xero connection details. None of this is secret; the refresh token lives in Credential Manager.</summary>
public sealed record XeroSettings
{
    public string? ClientId { get; init; }
    public string? TenantId { get; init; }
    public string? TenantName { get; init; }

    /// <summary>The Xero sales account documents are coded to, e.g. 200 Sales.</summary>
    public string? AccountCode { get; init; }
    public string? AccountName { get; init; }

    /// <summary>The branding theme quotes and invoices use (its look and payment services, e.g. Payfast); null for Xero's default.</summary>
    public string? BrandingThemeId { get; init; }
    public string? BrandingThemeName { get; init; }

    /// <summary>The expense account bills were last coded to, picked again next time.</summary>
    public string? BillAccountCode { get; init; }

    /// <summary>The bank account payments were last recorded into, picked again next time.</summary>
    public string? PaymentAccountId { get; init; }
}

/// <summary>
/// Everything the person has set up, each read from and written to the settings table as it's used. A new
/// setting is one property here: a record in Core (or here, when it's only about the app) with defaults, read
/// under a name, written under the same name.
/// </summary>
public sealed class AppSettings(LocalStore store)
{
    public DocumentSettings Documents
    {
        get => store.GetSetting<DocumentSettings>("documents") ?? new DocumentSettings();
        set => store.SetSetting("documents", value);
    }

    public TaskSettings Tasks
    {
        get => store.GetSetting<TaskSettings>("tasks") ?? new TaskSettings();
        set => store.SetSetting("tasks", value);
    }

    /// <summary>Whether the sidebar is tucked away and which of its groups are folded.</summary>
    public SidebarLayout? Sidebar
    {
        get => store.GetSetting<SidebarLayout>("sidebar");
        set => store.SetSetting("sidebar", value);
    }

    /// <summary>The file explorer: where client folders live, and how folders are shown.</summary>
    public Webignite.Core.Files.FileSettings Files
    {
        get => store.GetSetting<Webignite.Core.Files.FileSettings>("files") ?? new Webignite.Core.Files.FileSettings();
        set => store.SetSetting("files", value);
    }

    /// <summary>The screenshot shortcut and where screenshots are saved.</summary>
    public Webignite.Core.Notes.NoteSettings Notes
    {
        get => store.GetSetting<Webignite.Core.Notes.NoteSettings>("notes") ?? new Webignite.Core.Notes.NoteSettings();
        set => store.SetSetting("notes", value);
    }

    /// <summary>The palette being built in the Colours tool.</summary>
    public Webignite.Core.Tools.PaletteSettings Palette
    {
        get => store.GetSetting<Webignite.Core.Tools.PaletteSettings>("palette") ?? new Webignite.Core.Tools.PaletteSettings();
        set => store.SetSetting("palette", value);
    }

    /// <summary>Saved blocks of text to paste anywhere.</summary>
    public Webignite.Core.Tools.SnippetSettings Snippets
    {
        get => store.GetSetting<Webignite.Core.Tools.SnippetSettings>("snippets") ?? new Webignite.Core.Tools.SnippetSettings();
        set => store.SetSetting("snippets", value);
    }

    public Webignite.Core.Tools.ScreenshotSettings Screenshots
    {
        get => store.GetSetting<Webignite.Core.Tools.ScreenshotSettings>("screenshots") ?? new Webignite.Core.Tools.ScreenshotSettings();
        set => store.SetSetting("screenshots", value);
    }

    /// <summary>The signature drawn once for signing PDFs, as a PNG with a see-through background. Kept encrypted with the rest.</summary>
    public byte[]? Signature
    {
        get => store.GetSetting<SignatureSetting>("signature")?.Png;
        set => store.SetSetting("signature", new SignatureSetting(value));
    }

    /// <summary>Files opened lately from Files, newest first, for Recents in its side list.</summary>
    /// <summary>Colours picked lately, newest first, as codes like #E8590C.</summary>
    public IReadOnlyList<string> RecentColours
    {
        get => store.GetSetting<List<string>>("recent-colours") ?? [];
        set => store.SetSetting("recent-colours", value.ToList());
    }

    public IReadOnlyList<string> FileRecents
    {
        get => store.GetSetting<List<string>>("file-recents") ?? [];
        set => store.SetSetting("file-recents", value.ToList());
    }

    /// <summary>The tags for files and folders in Files, with their colours. Which files carry them is in the file_tags table.</summary>
    public IReadOnlyList<Webignite.Core.Files.FileTag> FileTags
    {
        get => store.GetSetting<List<Webignite.Core.Files.FileTag>>("file-tags") ?? [.. Webignite.Core.Files.FileTags.Defaults];
        set => store.SetSetting("file-tags", value.ToList());
    }

    /// <summary>The tabs that were open and which was showing.</summary>
    public Webignite.Core.Layout.TabSettings Tabs
    {
        get => store.GetSetting<Webignite.Core.Layout.TabSettings>("tabs") ?? new Webignite.Core.Layout.TabSettings();
        set => store.SetSetting("tabs", value);
    }

    /// <summary>Window size, position and last page, saved when the app closes.</summary>
    public WindowPlacement? Window
    {
        get => store.GetSetting<WindowPlacement>("window");
        set => store.SetSetting("window", value);
    }

    /// <summary>The mail categories you made, with their colours. The mail itself carries them as keywords on the server.</summary>
    public IReadOnlyList<MailCategory> MailCategories
    {
        get => store.GetSetting<List<MailCategory>>("mail-categories") ?? [];
        set => store.SetSetting("mail-categories", value.ToList());
    }

    /// <summary>Standard packages and items, for filling in quotes and invoices with one click.</summary>
    public IReadOnlyList<PriceEntry> PriceList
    {
        get => store.GetSetting<List<PriceEntry>>("price-list") ?? [];
        set => store.SetSetting("price-list", value.ToList());
    }

    /// <summary>When each customer last had a statement, so it's offered once a month.</summary>
    /// <summary>People whose mail needs no reply, up to the mail waved off (see <c>MorningSummary.NoReply</c>).</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> NoReplyNeeded
    {
        get => store.GetSetting<Dictionary<string, DateTimeOffset>>("no-reply-needed") ?? [];
        set => store.SetSetting("no-reply-needed", new Dictionary<string, DateTimeOffset>(value));
    }

    public IReadOnlyDictionary<string, DateOnly> StatementsSent
    {
        get => store.GetSetting<Dictionary<string, DateOnly>>("statements-sent") ?? [];
        set => store.SetSetting("statements-sent", new Dictionary<string, DateOnly>(value));
    }

    /// <summary>The invoice and quote email wording, pasted from Xero.</summary>
    public EmailTemplates EmailTemplates
    {
        get => store.GetSetting<EmailTemplates>("email-templates") ?? new EmailTemplates();
        set => store.SetSetting("email-templates", value);
    }

    /// <summary>How the income projection works out what is expected and what came in.</summary>
    public IncomePlan IncomePlan
    {
        get => store.GetSetting<IncomePlan>("income-plan") ?? new IncomePlan();
        set => store.SetSetting("income-plan", value);
    }

    public XeroSettings Xero
    {
        get => store.GetSetting<XeroSettings>("xero") ?? new XeroSettings();
        set
        {
            var clientChanged = value.ClientId != Xero.ClientId;
            store.SetSetting("xero", value);
            if (clientChanged)
                XeroClientChanged?.Invoke();
        }
    }

    /// <summary>Raised when the Xero Client ID changes, so the sign-in session is started afresh.</summary>
    public event Action? XeroClientChanged;

    public BackupSettings Backups
    {
        get => store.GetSetting<BackupSettings>("backups") ?? new BackupSettings();
        set => store.SetSetting("backups", value);
    }

    /// <summary>Earlier speed tests per domain, for "up from 38" and client reports.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SpeedRun>> SpeedHistory
    {
        get => store.GetSetting<Dictionary<string, List<SpeedRun>>>("speed-history")?
            .ToDictionary(h => h.Key, h => (IReadOnlyList<SpeedRun>)h.Value) ?? [];
        set => store.SetSetting("speed-history", value.ToDictionary(h => h.Key, h => h.Value.ToList()));
    }

    /// <summary>
    /// The colours the person gave to cards around the app, by the name each card is known under
    /// (<see cref="Controls.TileColours"/>). Widgets on their own pages keep theirs with the page.
    /// </summary>
    public IReadOnlyDictionary<string, Webignite.Core.Layout.TileLook> TileLooks
    {
        get => store.GetSetting<Dictionary<string, Webignite.Core.Layout.TileLook>>("tile-looks") ?? [];
        set => store.SetSetting("tile-looks", value.ToDictionary(l => l.Key, l => l.Value));
    }

    /// <summary>Where clients were dragged to on the Studio map, by client id; a client not in it sits where the layout puts it.</summary>
    public IReadOnlyDictionary<string, Webignite.Core.Studio.MapPlace> StudioPlaces
    {
        get => store.GetSetting<Dictionary<string, Webignite.Core.Studio.MapPlace>>("studio-places") ?? [];
        set => store.SetSetting("studio-places", value.ToDictionary(p => p.Key, p => p.Value));
    }

    /// <summary>Clients left off the Studio pages: contacts that came in from the books but aren't clients, such as a payment provider.</summary>
    public IReadOnlyList<string> StudioHidden
    {
        get => store.GetSetting<List<string>>("studio-hidden") ?? [];
        set => store.SetSetting("studio-hidden", value.ToList());
    }

    /// <summary>The person's own Google key for PageSpeed tests (free from Google); empty to test without one.</summary>
    public string SpeedKey
    {
        get => store.GetSetting<string>("speed-key") ?? "";
        set => store.SetSetting("speed-key", value.Trim());
    }

    /// <summary>Which websites are watched for being down or an expiring certificate, and what was found.</summary>
    public UptimeSettings Uptime
    {
        get => store.GetSetting<UptimeSettings>("uptime") ?? new UptimeSettings();
        set => store.SetSetting("uptime", value);
    }

    /// <summary>Light or dark, which dark look, and how much the app moves.</summary>
    public AppearanceSettings Appearance
    {
        get => store.GetSetting<AppearanceSettings>("appearance") ?? new AppearanceSettings();
        set
        {
            store.SetSetting("appearance", value);
            AppearanceChanged?.Invoke(value);
        }
    }

    /// <summary>The main window listens, so a change in Settings shows straight away.</summary>
    public event Action<AppearanceSettings>? AppearanceChanged;

    /// <summary>How the panes of a page are arranged, by page, for pages that have panes.</summary>
    public PanelLayout? PanelLayout(string page) =>
        store.GetSetting<Dictionary<string, PanelLayout>>("panels")?.GetValueOrDefault(page);

    public SectionLayout? SectionLayout(string page) =>
        store.GetSetting<Dictionary<string, SectionLayout>>("sections")?.GetValueOrDefault(page);

    public void SetSectionLayout(string page, SectionLayout layout)
    {
        var all = store.GetSetting<Dictionary<string, SectionLayout>>("sections") ?? [];
        all[page] = layout;
        store.SetSetting("sections", all);
    }

    public void SetPanelLayout(string page, PanelLayout layout)
    {
        var all = store.GetSetting<Dictionary<string, PanelLayout>>("panels") ?? [];
        all[page] = layout;
        store.SetSetting("panels", all);
    }

    /// <summary>Mail set to send later, waiting as drafts.</summary>
    public SendLaterSettings SendLater
    {
        get => store.GetSetting<SendLaterSettings>("mail-send-later") ?? new SendLaterSettings();
        set => store.SetSetting("mail-send-later", value);
    }

    /// <summary>Text written often, put into a mail from the editor.</summary>
    public IReadOnlyList<SavedReply> SavedReplies
    {
        get => store.GetSetting<List<SavedReply>>("mail-saved-replies") ?? [];
        set => store.SetSetting("mail-saved-replies", value.ToList());
    }

    /// <summary>You and your partner, for projects: who made and changed what.</summary>
    public TeamSettings Team
    {
        get => store.GetSetting<TeamSettings>("team") ?? new TeamSettings();
        set => store.SetSetting("team", value);
    }

    /// <summary>Projects kept to start others from.</summary>
    /// <summary>The person's own pages and the widgets on each, in the order the sidebar lists them.</summary>
    public IReadOnlyList<MyPage> MyPages
    {
        get => store.GetSetting<List<MyPage>>("my-pages") ?? [];
        set => store.SetSetting("my-pages", value.ToList());
    }

    /// <summary>The widgets the person put on Overview, under its own parts; null until they add one.</summary>
    public MyPage? OverviewBoard
    {
        get => store.GetSetting<MyPage>("overview-board");
        set => store.SetSetting("overview-board", value ?? OverviewParts.EmptyBoard());
    }

    /// <summary>Which of Overview's own parts show.</summary>
    public OverviewParts OverviewParts
    {
        get => store.GetSetting<OverviewParts>("overview-parts") ?? new OverviewParts();
        set => store.SetSetting("overview-parts", value);
    }

    /// <summary>How quote and invoice mails look: logo, footer details, banking details and terms. Shared with the other PC.</summary>
    public MailBrand MailBrand
    {
        get => store.GetSetting<MailBrand>("mail-brand") ?? new MailBrand();
        set => store.SetSetting("mail-brand", value);
    }

    /// <summary>Where this PC is in the setup guide; null when it was never started here.</summary>
    public Webignite.Core.Accounts.SetupProgress? SetupGuide
    {
        get => store.GetSetting<Webignite.Core.Accounts.SetupProgress>("setup-guide");
        set => store.SetSetting("setup-guide", value);
    }

    public IReadOnlyList<ProjectTemplate> ProjectTemplates
    {
        get => store.GetSetting<List<ProjectTemplate>>("project-templates") ?? [];
        set => store.SetSetting("project-templates", value.ToList());
    }

    /// <summary>How Calendar and Planner were sized by dragging.</summary>
    public CalendarSizes CalendarSizes
    {
        get => store.GetSetting<CalendarSizes>("calendar-sizes") ?? new CalendarSizes();
        set => store.SetSetting("calendar-sizes", value);
    }

    /// <summary>How Planner's Home was left: which projects its Projects card shows.</summary>
    /// <summary>ChatGPT's tunnel on this PC: its id and whether it runs. The key is in Windows Credential Manager.</summary>
    public ChatGptTunnelSettings ChatGptTunnel
    {
        get => store.GetSetting<ChatGptTunnelSettings>("chatgpt-tunnel") ?? new ChatGptTunnelSettings();
        set => store.SetSetting("chatgpt-tunnel", value);
    }

    /// <summary>The mailboxes folded up to their name and Inbox in Mail's folder list, by id.</summary>
    public IReadOnlyList<string> FoldedMailboxes
    {
        get => store.GetSetting<List<string>>("mail-folded") ?? [];
        set => store.SetSetting("mail-folded", value.ToList());
    }

    public HomeSettings Home
    {
        get => store.GetSetting<HomeSettings>("planner-home") ?? new HomeSettings();
        set => store.SetSetting("planner-home", value);
    }

    /// <summary>Each project's own list or board, sort, grouping and folded headings, by project id.</summary>
    public IReadOnlyDictionary<string, ProjectLayout> ProjectLayouts
    {
        get => store.GetSetting<Dictionary<string, ProjectLayout>>("project-layouts") ?? [];
        set => store.SetSetting("project-layouts", value.ToDictionary(p => p.Key, p => p.Value));
    }

    /// <summary>List or board on the Projects page, for a project not yet shown either way.</summary>
    public ProjectViewSettings ProjectView
    {
        get => store.GetSetting<ProjectViewSettings>("project-view") ?? new ProjectViewSettings();
        set => store.SetSetting("project-view", value);
    }

    /// <summary>Spell check while writing a mail.</summary>
    public SpellCheckSettings SpellCheck
    {
        get => store.GetSetting<SpellCheckSettings>("spell-check") ?? new SpellCheckSettings();
        set => store.SetSetting("spell-check", value);
    }

    /// <summary>Whole mails kept to start others from, put in from the editor's Templates menu.</summary>
    public IReadOnlyList<MailTemplate> MailTemplates
    {
        get => store.GetSetting<List<MailTemplate>>("mail-templates") ?? [];
        set => store.SetSetting("mail-templates", value.ToList());
    }

    /// <summary>Snoozed mail waiting in the Snoozed folder, and mail that came back from snooze lately.</summary>
    public SnoozeSettings Snooze
    {
        get => store.GetSetting<SnoozeSettings>("mail-snooze") ?? new SnoozeSettings();
        set => store.SetSetting("mail-snooze", value);
    }

    /// <summary>Mail rules and how far through each inbox they have looked.</summary>
    public MailRuleSettings MailRules
    {
        get => store.GetSetting<MailRuleSettings>("mail-rules") ?? new MailRuleSettings();
        set => store.SetSetting("mail-rules", value);
    }

    /// <summary>How the message list looks: which lists show one row per mail rather than conversations.</summary>
    public MailViewSettings MailView
    {
        get => store.GetSetting<MailViewSettings>("mail-view") ?? new MailViewSettings();
        set => store.SetSetting("mail-view", value);
    }

    /// <summary>How often mail is checked in the background, and what was already announced.</summary>
    public MailCheckSettings MailCheck
    {
        get => store.GetSetting<MailCheckSettings>("mail-check") ?? new MailCheckSettings();
        set => store.SetSetting("mail-check", value);
    }
}

/// <summary>The saved signature, as stored.</summary>
public sealed record SignatureSetting(byte[]? Png);
