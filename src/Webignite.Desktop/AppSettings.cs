using Webignite.Core.Backups;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Layout;
using Webignite.Core.Mail;
using Webignite.Core.Tasks;
using Webignite.Core.Web;
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

    public void SetPanelLayout(string page, PanelLayout layout)
    {
        var all = store.GetSetting<Dictionary<string, PanelLayout>>("panels") ?? [];
        all[page] = layout;
        store.SetSetting("panels", all);
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
