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
        var (news, problem) = await PaymentWatch.RunAsync(services);
        if (byHand && problem is not null)
            services.ShowNote?.Invoke("Couldn't check payments", problem, null);
        foreach (var payment in news.Take(3))
            services.ShowNote?.Invoke(payment.Title, payment.Text, () => Selected = Modules.Single(m => m.Page == Invoices));
        if (news.Count > 3)
            services.ShowNote?.Invoke($"{news.Count - 3} more payments came in", "Open Invoices to see them.", () => Selected = Modules.Single(m => m.Page == Invoices));
        if (byHand && problem is null && news.Count == 0)
            services.ShowNote?.Invoke("Payments checked", "No new payments in Xero.", null);
        Overview.Refresh();
        Invoices.Refresh();
        Quotes.Refresh();
    }

    /// <summary>Shows a note for each site that went down, came back, or has a certificate about to run out.</summary>
    private void AnnounceSites(IReadOnlyList<Webignite.Core.Web.UptimeAlert> alerts)
    {
        foreach (var alert in alerts.Take(3))
            services.ShowNote?.Invoke(alert.Title, alert.Text, () => Selected = Modules.Single(m => m.Page == SiteCheck));
        if (alerts.Count > 3)
            services.ShowNote?.Invoke($"{alerts.Count - 3} more sites changed", "Open Site check to see them.", () => Selected = Modules.Single(m => m.Page == SiteCheck));
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
            await Mail.SyncAll();
            var settings = services.MailCheckSettings;
            var inboxes = new List<(string, IReadOnlyList<Webignite.Core.Mail.MailSummary>)>();
            foreach (var account in services.Store.ListMailAccounts())
            {
                if (services.Store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == Webignite.Core.Mail.FolderRole.Inbox) is { } inbox)
                    inboxes.Add((account.Id, services.Store.ListMailSummaries(account.Id, inbox.Path, 200)));
            }
            UpdateMailBadge();

            var found = Webignite.Core.Mail.NewMail.Find(settings.Announced, inboxes);
            services.MailCheckSettings = settings with { Announced = found.Announced };
            var active = WindowIsActive?.Invoke() == true;
            if (found.Fresh.Count == 0 || (active && Selected == mailModule))
                return;
            if (!active)
                FlashTaskbar?.Invoke();
            if (settings.Notify)
            {
                var (title, text) = Webignite.Core.Mail.NewMail.Note(found.Fresh);
                var first = found.Fresh[0];
                services.ShowNote?.Invoke(title, text, () => services.OpenMail?.Invoke(first));
            }
        }
        finally
        {
            checkingMail = false;
        }
    }

    public UpdateViewModel Updates { get; } = new();

    public MainViewModel(AppServices services)
    {
        this.services = services;
        services.CatchUp();
        _ = Updates.CheckAsync();
        _ = services.BackUpAsync();
        // Once an hour, check whether the daily backup is due, for when the app stays open for days.
        backupTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        backupTimer.Tick += (_, _) => _ = services.BackUpAsync();
        backupTimer.Start();
        // Mail is checked in the background every few minutes (Settings), even when another page is open.
        mailTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        mailTimer.Tick += (_, _) =>
        {
            if (DateTimeOffset.Now - lastMailCheck >= Webignite.Core.Mail.NewMail.Interval(services.MailCheckSettings))
                _ = CheckMailAsync();
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
        uptimeTimer.Tick += async (_, _) =>
        {
            if (!Webignite.Core.Web.Uptime.Due(services.UptimeSettings, UptimeWatch.LastRun, DateTimeOffset.Now))
                return;
            AnnounceSites(await UptimeWatch.RunAsync(services));
            SiteCheck.RefreshWatched();
        };
        uptimeTimer.Start();
        // Payments are read from Xero when the app opens and every half hour after that.
        Overview.CheckPayments = () => CheckPaymentsAsync(byHand: true);
        paymentTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        paymentTimer.Tick += async (_, _) =>
        {
            if (services.XeroApi is not null && PaymentWatch.Due(DateTimeOffset.Now))
                await CheckPaymentsAsync();
        };
        paymentTimer.Start();
        Markup = new MarkupViewModel(services);
        Modules =
        [
            new Module("Overview", Overview),
            new Module("Clients", Clients),
            new Module("Quotes", Quotes),
            new Module("Invoices", Invoices),
            new Module("Retainers", new RetainersViewModel(services)),
            new Module("Domains and hosting", new RenewalsViewModel(services)),
            new Module("Money", new MoneyViewModel(services)),
            new Module("Reports", new ReportsViewModel(services)),
            new Module("To-do", Todo),
            new Module("Calendar", new CalendarViewModel(services)),
            new Module("Planner", new PlannerViewModel(services)),
            new Module("To approve", Approvals),
            new Module("Mail", Mail, fullBleed: true),
            new Module("Site check", SiteCheck),
            new Module("Mark-up", Markup),
            new Module("Tools", Tools),
            new Module("Settings", new SettingsViewModel(services)),
        ];
        Selected = Modules[0];
        mailModule = Modules.Single(m => m.Page == Mail);
        _ = CheckMailAsync();
        services.OpenMail = summary =>
        {
            Selected = mailModule;
            Mail.OpenMessage(summary);
        };
        services.ComposeMail = (mail, note, afterSent) =>
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
        services.OpenClient = client =>
        {
            Selected = Modules.Single(m => m.Title == "Clients");
            Clients.OpenClient(services.Store.ListClients().FirstOrDefault(c => c.Id == client.Id) ?? client);
        };
        services.AddToInvoice = (amount, description) =>
        {
            if (Invoices.Editor is { } open && !open.AddAmount(amount, description))
                return "The open invoice is already in Xero, so it can't be changed here. Close it first, then try again.";
            if (Invoices.Editor is null)
            {
                Invoices.NewCommand.Execute(null);
                Invoices.Editor!.AddAmount(amount, description);
            }
            Selected = Modules.Single(m => m.Title == "Invoices");
            return null;
        };
        services.Navigate = (target, mail) =>
        {
            if (target == Webignite.Core.Overview.SummaryTarget.Mail && mail is not null)
            {
                services.OpenMail?.Invoke(mail);
                return;
            }
            var title = target switch
            {
                Webignite.Core.Overview.SummaryTarget.Invoices => "Invoices",
                Webignite.Core.Overview.SummaryTarget.Approvals => "To approve",
                Webignite.Core.Overview.SummaryTarget.Mail => "Mail",
                Webignite.Core.Overview.SummaryTarget.Renewals => "Domains and hosting",
                Webignite.Core.Overview.SummaryTarget.Money => "Money",
                Webignite.Core.Overview.SummaryTarget.Clients => "Clients",
                Webignite.Core.Overview.SummaryTarget.SiteCheck => "Site check",
                _ => "Settings",
            };
            Selected = Modules.Single(m => m.Title == title);
        };
        Search = new QuickSearchViewModel(services, OpenHit);
        services.WriteMail = to =>
        {
            Selected = mailModule;
            Mail.WriteTo(to);
        };
    }

    public QuickSearchViewModel Search { get; }

    private void OpenHit(Webignite.Core.Search.SearchHit hit)
    {
        Module Page(string title) => Modules.Single(m => m.Title == title);
        switch (hit.Kind)
        {
            case Webignite.Core.Search.HitKind.Client:
                Selected = Page("Clients");
                Clients.OpenClient(hit.Client!);
                break;
            case Webignite.Core.Search.HitKind.Quote:
                Selected = Page("Quotes");
                Quotes.OpenDocument(hit.Document!);
                break;
            case Webignite.Core.Search.HitKind.Invoice:
                Selected = Page("Invoices");
                Invoices.OpenDocument(hit.Document!);
                break;
            case Webignite.Core.Search.HitKind.Mail:
                services.OpenMail?.Invoke(hit.Mail!);
                break;
            case Webignite.Core.Search.HitKind.Todo:
                Selected = Page("To-do");
                break;
            default:
                Selected = Page("Domains and hosting");
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
    public IReadOnlyList<Module> Modules { get; }

    [ObservableProperty]
    public partial Module Selected { get; set; }

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
            case CalendarViewModel calendar: calendar.Refresh(); break;
            case PlannerViewModel planner: planner.Refresh(); break;
            case OverviewViewModel overview: overview.Refresh(); break;
            case ClientsViewModel clients: clients.Refresh(); break;
            case MoneyViewModel money: money.Refresh(); break;
            case ReportsViewModel reports: reports.Refresh(); break;
            case ApprovalsViewModel approvals: approvals.Refresh(); break;
            case MailViewModel mail: mail.Reload(); _ = mail.SyncAll(); break;
            case SettingsViewModel settings: settings.Refresh(); break;
        }
    }
}
