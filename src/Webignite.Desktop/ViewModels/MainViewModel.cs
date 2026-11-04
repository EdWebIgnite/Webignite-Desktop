using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly Avalonia.Threading.DispatcherTimer backupTimer;
    private readonly Avalonia.Threading.DispatcherTimer mailTimer;
    private readonly Avalonia.Threading.DispatcherTimer uptimeTimer;
    private readonly Avalonia.Threading.DispatcherTimer paymentTimer;

    /// <summary>Reads payments from Xero, refreshes the pages that show money owed and announces what came in.</summary>
    public async Task CheckPaymentsAsync(bool byHand = false)
    {
        var (news, problem) = await services.Work.Run("Checking Xero for payments", () => services.Payments.RunAsync());
        if (byHand && problem is not null)
            services.ShowNote?.Invoke("Couldn't check payments", problem, null);
        foreach (var payment in news.Take(3))
            services.ShowNote?.Invoke(payment.Title, payment.Text, () => Selected = this[Page.Invoices]);
        if (news.Count > 3)
            services.ShowNote?.Invoke($"{news.Count - 3} more payments came in", "Open Invoices to see them.", () => Selected = this[Page.Invoices]);
        if (byHand && problem is null && news.Count == 0)
            services.ShowNote?.Invoke("Payments checked", "No new payments in Xero.", null);
        Overview.Refresh();
        Invoices.Refresh();
        Quotes.Refresh();
    }

    /// <summary>Shows a note for each site that went down, came back, or has a certificate about to run out.</summary>
    private async Task CheckSitesAsync()
    {
        AnnounceSites(await services.SiteWatch.RunAsync());
        SiteCheck.RefreshWatched();
    }

    private void AnnounceSites(IReadOnlyList<Webignite.Core.Web.UptimeAlert> alerts)
    {
        foreach (var alert in alerts.Take(3))
            services.ShowNote?.Invoke(alert.Title, alert.Text, () => Selected = this[Page.SiteCheck]);
        if (alerts.Count > 3)
            services.ShowNote?.Invoke($"{alerts.Count - 3} more sites changed", "Open Site check to see them.", () => Selected = this[Page.SiteCheck]);
        if (alerts.Any(a => a.Problem) && WindowIsActive?.Invoke() != true)
            FlashTaskbar?.Invoke();
    }
    private DateTimeOffset lastMailCheck = DateTimeOffset.MinValue;
    private bool checkingMail;
    private readonly Module mailModule;

    /// <summary>Set by the main window: whether it's the window in front, so a note isn't shown for mail already on screen.</summary>
    public Func<bool>? WindowIsActive { get; set; }

    /// <summary>Set by the main window: makes the taskbar button flash when new mail comes in while the app is behind.</summary>
    public Action? FlashTaskbar { get; set; }

    /// <summary>Unread mail across all inboxes, shown beside Mail in the sidebar.</summary>
    public void UpdateMailBadge() =>
        mailModule.Badge = services.Store.ListMailAccounts().Sum(a =>
            services.Store.ListMailFolders(a.Id).FirstOrDefault(f => f.Role == Webignite.Core.Mail.FolderRole.Inbox)?.Unread ?? 0);

    /// <summary>Fetches new mail, updates the unread count and announces mail that just arrived.</summary>
    public async Task CheckMailAsync()
    {
        if (checkingMail)
            return;
        checkingMail = true;
        lastMailCheck = DateTimeOffset.Now;
        try
        {
            using (services.Work.Start("Checking mail"))
                await Mail.SyncAll();
            var settings = services.Settings.MailCheck;
            var inboxes = new List<(string, IReadOnlyList<Webignite.Core.Mail.MailSummary>)>();
            foreach (var account in services.Store.ListMailAccounts())
            {
                if (services.Store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == Webignite.Core.Mail.FolderRole.Inbox) is { } inbox)
                    inboxes.Add((account.Id, services.Store.ListMailSummaries(account.Id, inbox.Path, 200)));
            }
            UpdateMailBadge();

            var found = Webignite.Core.Mail.NewMail.Find(settings.Announced, inboxes);
            services.Settings.MailCheck = settings with { Announced = found.Announced };
            var active = WindowIsActive?.Invoke() == true;
            if (found.Fresh.Count == 0 || (active && Selected == mailModule))
                return;
            if (!active)
                FlashTaskbar?.Invoke();
            if (settings.Notify)
            {
                var (title, text) = Webignite.Core.Mail.NewMail.Note(found.Fresh);
                var first = found.Fresh[0];
                services.ShowNote?.Invoke(title, text, () => services.Go.Mail(first));
            }
        }
        finally
        {
            checkingMail = false;
        }
    }

    public UpdateViewModel Updates { get; } = new();

    public WorkInProgress Work => services.Work;

    public MainViewModel(AppServices services)
    {
        this.services = services;
        services.CatchUp();
        Updates.CheckAsync().Forget("Checking for an update");
        services.BackUpAsync().Forget("Backing up");
        // Once an hour, check whether the daily backup is due, for when the app stays open for days.
        backupTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        backupTimer.Tick += (_, _) => services.BackUpAsync().Forget("Backing up");
        backupTimer.Start();
        // Mail is checked in the background every few minutes (Settings), even when another page is open.
        mailTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        mailTimer.Tick += (_, _) =>
        {
            services.CatchUp(); // a moment's work only after midnight; otherwise it returns at once
            if (DateTimeOffset.Now - lastMailCheck >= Webignite.Core.Mail.NewMail.Interval(services.Settings.MailCheck))
                CheckMailAsync().Forget("Checking mail");
        };
        mailTimer.Start();
        Quotes = new DocumentsViewModel(DocumentKind.Quote, services);
        Invoices = new DocumentsViewModel(DocumentKind.Invoice, services);
        Todo = new TodoViewModel(services);
        Overview = new OverviewViewModel(services);
        Clients = new ClientsViewModel(services);
        Mail = new MailViewModel(services);
        Approvals = new ApprovalsViewModel(services);
        Tools = new ToolsViewModel(services);
        SiteCheck = new SiteCheckViewModel(services);
        SiteCheck.Announce = AnnounceSites;
        SiteCheck.RefreshWatched();
        // Client websites are visited every 15 minutes while the app is open.
        uptimeTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        uptimeTimer.Tick += (_, _) =>
        {
            if (services.SiteWatch.Due(DateTimeOffset.Now))
                CheckSitesAsync().Forget("Checking client websites");
        };
        uptimeTimer.Start();
        // Payments are read from Xero when the app opens and every half hour after that.
        Overview.CheckPayments = () => CheckPaymentsAsync(byHand: true);
        paymentTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        paymentTimer.Tick += (_, _) =>
        {
            if (services.XeroApi is not null && services.Payments.Due(DateTimeOffset.Now))
                CheckPaymentsAsync().Forget("Checking payments in Xero");
        };
        paymentTimer.Start();
        Markup = new MarkupViewModel(services);
        Time = new TimeViewModel(services);
        // The sidebar, in groups. Headings sit between the groups; the pages keep the order shown here.
        object[] nav =
        [
            new Module(Page.Overview, "Overview", Overview, "Overview"),
            new Module(Page.Approvals, "To approve", Approvals, "Approve"),
            new Module(Page.Mail, "Mail", Mail, "Mail", fullBleed: true),
            new NavGroup("client-work", "CLIENT WORK"),
            new Module(Page.Clients, "Clients", Clients, "Clients", fullBleed: true),
            new Module(Page.Retainers, "Retainers", new RetainersViewModel(services), "Retainers"),
            new Module(Page.Renewals, "Domains and hosting", new RenewalsViewModel(services), "Domains"),
            new NavGroup("billing", "BILLING"),
            new Module(Page.Quotes, "Quotes", Quotes, "Quotes"),
            new Module(Page.Invoices, "Invoices", Invoices, "Invoices"),
            new Module(Page.Money, "Money", new MoneyViewModel(services), "Money"),
            new Module(Page.Reports, "Reports", new ReportsViewModel(services), "Reports"),
            new NavGroup("planning", "PLANNING"),
            new Module(Page.Todo, "To-do", Todo, "Todo"),
            new Module(Page.Calendar, "Calendar", new CalendarViewModel(services), "Calendar"),
            new Module(Page.Planner, "Planner", new PlannerViewModel(services), "Planner"),
            new Module(Page.Time, "Time", Time, "Time"),
            new NavGroup("web-tools", "WEB TOOLS"),
            new Module(Page.SiteCheck, "Site check", SiteCheck, "SiteCheck"),
            new Module(Page.Markup, "Mark-up", Markup, "Markup"),
            new Module(Page.Tools, "Tools", Tools, "Tools"),
            new NavGroup("end", ""),
            new Module(Page.Settings, "Settings", new SettingsViewModel(services), "Settings"),
        ];
        NavGroup? group = null;
        foreach (var item in nav)
        {
            if (item is NavGroup heading)
                group = heading;
            else if (item is Module module)
                module.Group = group;
        }
        Modules = [.. nav.OfType<Module>()];
        Sidebar = new SidebarViewModel(services.Settings, nav);
        Selected = Modules[0];
        mailModule = this[Page.Mail];
        CheckMailAsync().Forget("Checking mail");

        // How other pages get here. Each one lands on the right page first, then opens the thing.
        var go = services.Go;
        go.OpenPage = page => Selected = this[page];
        go.OpenMailAction = summary =>
        {
            Selected = mailModule;
            Mail.OpenMessage(summary);
        };
        go.WriteMailAction = to =>
        {
            Selected = mailModule;
            Mail.WriteTo(to);
        };
        go.ComposeMailAction = (mail, note, afterSent) =>
        {
            Selected = mailModule;
            if (!Mail.Compose(mail, afterSent: afterSent))
                return false;
            if (note is not null && Mail.Composer is { } composer)
            {
                composer.Message = note;
                composer.MessageIsProblem = true;
            }
            return true;
        };
        go.OpenDocumentAction = stored =>
        {
            var list = stored.Document.Kind == DocumentKind.Quote ? Quotes : Invoices;
            Selected = this[stored.Document.Kind == DocumentKind.Quote ? Page.Quotes : Page.Invoices];
            list.OpenDocument(stored);
        };
        go.OpenClientAction = client =>
        {
            Selected = this[Page.Clients];
            Clients.OpenClient(services.Store.GetClient(client.Id) ?? client);
        };
        go.AddToInvoiceAction = (amount, description) =>
        {
            if (Invoices.Editor is { } open && !open.AddAmount(amount, description))
                return "The open invoice is already in Xero, so it can't be changed here. Close it first, then try again.";
            if (Invoices.Editor is null)
            {
                Invoices.NewCommand.Execute(null);
                Invoices.Editor!.AddAmount(amount, description);
            }
            Selected = this[Page.Invoices];
            return null;
        };
        Search = new QuickSearchViewModel(services, OpenHit);
    }

    /// <summary>The sidebar entry for a page.</summary>
    public Module this[Page page] => Modules.Single(m => m.Key == page);

    public QuickSearchViewModel Search { get; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenTime() => Selected = this[Page.Time];

    private void OpenHit(Webignite.Core.Search.SearchHit hit)
    {
        switch (hit.Kind)
        {
            case Webignite.Core.Search.HitKind.Client:
                services.Go.Client(hit.Client!);
                break;
            case Webignite.Core.Search.HitKind.Quote:
            case Webignite.Core.Search.HitKind.Invoice:
                services.Go.Document(hit.Document!);
                break;
            case Webignite.Core.Search.HitKind.Mail:
                services.Go.Mail(hit.Mail!);
                break;
            case Webignite.Core.Search.HitKind.Todo:
                services.Go.To(Page.Todo);
                break;
            default:
                services.Go.To(Page.Renewals);
                break;
        }
    }

    public DocumentsViewModel Quotes { get; }
    public DocumentsViewModel Invoices { get; }
    public TodoViewModel Todo { get; }
    public OverviewViewModel Overview { get; }
    public ClientsViewModel Clients { get; }
    public MailViewModel Mail { get; }
    public ApprovalsViewModel Approvals { get; }
    public ToolsViewModel Tools { get; }
    public SiteCheckViewModel SiteCheck { get; }
    public MarkupViewModel Markup { get; }
    public TimeViewModel Time { get; }
    public IReadOnlyList<Module> Modules { get; }

    /// <summary>The sidebar itself: hidden or showing, and which groups are folded.</summary>
    public SidebarViewModel Sidebar { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNav))]
    public partial Module Selected { get; set; }

    /// <summary>The sidebar's selection. Headings can't be picked, so anything that isn't a page is ignored.</summary>
    public object? SelectedNav
    {
        get => Selected;
        set
        {
            if (value is Module module)
                Selected = module;
            else
                OnPropertyChanged();
        }
    }

    /// <summary>
    /// Runs when the window is closed: saves open quotes, invoices and the open client, sends a mail still in its
    /// undo wait and saves the mail being written. Returns what couldn't be kept, or null when nothing is lost.
    /// </summary>
    public async Task<string?> PrepareToCloseAsync()
    {
        foreach (var money in Modules.Select(m => m.Page).OfType<MoneyViewModel>())
            money.SaveTyped();
        Invoices.Editor?.Keep();
        Quotes.Editor?.Keep();
        Approvals.Editor?.Keep();
        var problem = Clients.Detail is { } client && !client.Leave()
            ? $"{client.Title} has something to fix before it can be saved."
            : null;
        return await Mail.PrepareToCloseAsync() ?? problem;
    }

    // These pages show things other pages change, so they look again whenever they're opened.
    partial void OnSelectedChanged(Module value)
    {
        services.CatchUp();
        if (mailModule is not null)
            UpdateMailBadge();
        switch (value.Page)
        {
            case DocumentsViewModel documents: documents.Refresh(); break;
            case RetainersViewModel retainers: retainers.Refresh(); break;
            case RenewalsViewModel renewals: renewals.Refresh(); break;
            case TodoViewModel todo: todo.Refresh(); break;
            case TimeViewModel time: time.Refresh(); break;
            case CalendarViewModel calendar: calendar.Refresh(); break;
            case PlannerViewModel planner: planner.Refresh(); break;
            case OverviewViewModel overview: overview.Refresh(); break;
            case ClientsViewModel clients: clients.Refresh(); break;
            case MoneyViewModel money: money.Refresh(); break;
            case ReportsViewModel reports: reports.Refresh(); break;
            case ApprovalsViewModel approvals: approvals.Refresh(); break;
            case MailViewModel mail: mail.Reload(); mail.SyncAll().Forget("Checking mail"); break;
            case SettingsViewModel settings: settings.Refresh(); break;
        }
    }
}
