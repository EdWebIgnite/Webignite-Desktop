using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppServices services;

    /// <summary>A page nobody has looked at for this long is let go, and built again when next opened.</summary>
    public static readonly TimeSpan PageIdle = TimeSpan.FromMinutes(10);

    /// <summary>Mail checks, payments, website checks and backups: they run whichever pages are built.</summary>
    public BackgroundJobs Jobs { get; }

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
        // Only pages already built need telling; the rest read the latest when opened.
        foreach (var page in Built<IRefreshable>(Page.Overview, Page.Invoices, Page.Quotes))
            page.Refresh();
    }

    /// <summary>Tells the open to-do, calendar, planner and overview pages that something was added from elsewhere.</summary>
    public void RefreshPlanning()
    {
        foreach (var page in Built<IRefreshable>(Page.Todo, Page.Calendar, Page.Planner, Page.Overview))
            page.Refresh();
    }

    /// <summary>Brings Xero's customers in as clients and tells the Clients pages that are open.</summary>
    private async Task CheckXeroClientsAsync()
    {
        var news = await services.Work.Run("Bringing clients in from Xero", () => services.XeroClients.RunAsync());
        if (news.Added + news.Filled == 0)
            return;
        foreach (var page in Built<ClientsViewModel>(Page.Clients))
            page.Refresh();
        if (news.Added > 0)
            services.ShowNote?.Invoke("Clients from Xero", news.Summary + ".", () => Selected = this[Page.Clients]);
    }

    /// <summary>Shows a note for each site that went down, came back, or has a certificate about to run out.</summary>
    private async Task CheckSitesAsync()
    {
        AnnounceSites(await services.SiteWatch.RunAsync());
        foreach (var page in Built<SiteCheckViewModel>(Page.SiteCheck))
            page.RefreshWatched();
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
        Jobs.LastMailCheck = DateTimeOffset.Now;
        try
        {
            // With Mail open its list and status update too; otherwise the check runs on its own.
            using (services.Work.Start("Checking mail"))
            {
                var open = Built<MailViewModel>(Page.Mail).ToList();
                if (open.Count > 0)
                {
                    await open[0].SyncAll();
                    foreach (var other in open.Skip(1))
                        other.Reload();
                }
                else
                {
                    await services.MailCheck.RunAsync();
                }
            }
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
            if (found.Fresh.Count == 0 || (active && Current.Key == Page.Mail))
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
        // The sidebar, in groups. Headings sit between the groups; the pages keep the order shown here.
        // Each page is built the first time it is opened, so starting the app builds Overview and nothing else.
        object[] nav =
        [
            new Module(Page.Overview, "Overview", () => new OverviewViewModel(services) { CheckPayments = () => CheckPaymentsAsync(byHand: true) }, "Overview"),
            new Module(Page.Approvals, "To approve", () => new ApprovalsViewModel(services), "Approve"),
            new Module(Page.Mail, "Mail", () => new MailViewModel(services), "Mail", fullBleed: true),
            new NavGroup("client-work", "CLIENT WORK"),
            new Module(Page.Clients, "Clients", () => new ClientsViewModel(services), "Clients", fullBleed: true),
            new Module(Page.Retainers, "Retainers", () => new RetainersViewModel(services), "Retainers"),
            new Module(Page.Renewals, "Domains and hosting", () => new RenewalsViewModel(services), "Domains"),
            new NavGroup("billing", "BILLING"),
            new Module(Page.Quotes, "Quotes", () => new DocumentsViewModel(DocumentKind.Quote, services), "Quotes"),
            new Module(Page.Invoices, "Invoices", () => new DocumentsViewModel(DocumentKind.Invoice, services), "Invoices"),
            new Module(Page.Money, "Money", () => new MoneyViewModel(services), "Money"),
            new Module(Page.Reports, "Reports", () => new ReportsViewModel(services), "Reports"),
            new NavGroup("planning", "PLANNING"),
            new Module(Page.Todo, "To-do", () => new TodoViewModel(services), "Todo"),
            new Module(Page.Calendar, "Calendar", () => new CalendarViewModel(services), "Calendar"),
            new Module(Page.Planner, "Planner", () => new PlannerViewModel(services), "Planner"),
            new Module(Page.Time, "Time", () => new TimeViewModel(services), "Time"),
            new NavGroup("computer", "COMPUTER"),
            new Module(Page.Files, "Files", () => new FilesViewModel(services), "Folder", fullBleed: true),
            new Module(Page.Pdf, "PDF tools", () => new PdfViewModel(services), "Quotes", fullBleed: true),
            new Module(Page.Notes, "Notes", () => new NotesViewModel(services), "Notes", fullBleed: true),
            new ToolLink(ToolKind.Calculator, "Calculator", "Calculator"),
            new ToolLink(ToolKind.Passwords, "Password generator", "Key"),
            new ToolLink(ToolKind.Colours, "Colours", "Palette"),
            new ToolLink(ToolKind.Clipboard, "Clipboard history", "Clipboard"),
            new NavGroup("web-tools", "WEB TOOLS"),
            new Module(Page.SiteCheck, "Site check", () => BuildSiteCheck(services), "SiteCheck"),
            new Module(Page.Markup, "Mark-up", () => new MarkupViewModel(services), "Markup"),
            new ToolLink(ToolKind.Images, "Website images", "Picture"),
            new ToolLink(ToolKind.Qr, "QR code", "Qr"),
            new NavGroup("end", ""),
            new Module(Page.Settings, "Settings", () => new SettingsViewModel(services), "Settings"),
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
        Open = [.. Modules.Select(m => m.Main)];
        Current = Modules[0].Main;
        Selected = Modules[0];
        mailModule = this[Page.Mail];
        Jobs = new BackgroundJobs(services)
        {
            CheckMail = CheckMailAsync,
            CheckPayments = () => CheckPaymentsAsync(),
            CheckSites = CheckSitesAsync,
            CheckXeroClients = CheckXeroClientsAsync,
            EveryMinute = () => ReleaseIdle(PageIdle),
        };
        Jobs.Start();

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
            Clients.OpenClient(services.Clients.Get(client.Id) ?? client);
        };
        go.OpenClientInNewTabAction = client =>
            ((ClientsViewModel)OpenInNewTab(this[Page.Clients]).Page).OpenClient(services.Clients.Get(client.Id) ?? client);
        go.OpenClientInNewWindowAction = client =>
        {
            var host = OpenInNewTab(this[Page.Clients]);
            ((ClientsViewModel)host.Page).OpenClient(services.Clients.Get(client.Id) ?? client);
            PopOut(host);
        };
        go.OpenMailInNewTabAction = summary => ((MailViewModel)OpenInNewTab(mailModule).Page).OpenMessage(summary);
        go.OpenMailInNewWindowAction = summary =>
        {
            var host = OpenInNewTab(mailModule);
            ((MailViewModel)host.Page).OpenMessage(summary);
            PopOut(host);
        };
        go.OpenPdfAction = paths =>
        {
            // Opened in a PDF tools tab of its own when one already has work in it.
            var host = LastUsed(this[Page.Pdf]);
            if (host.IsBuilt && host.Page is PdfViewModel { HasPages: true })
                host = OpenInNewTab(this[Page.Pdf]);
            else
                Selected = this[Page.Pdf];
            ((PdfViewModel)host.Page).Open(paths).Forget("Opening PDFs", tell: true);
        };
        go.OpenNoteAction = id =>
        {
            Selected = this[Page.Notes];
            PageOf<NotesViewModel>(Page.Notes).OpenNote(id);
        };
        go.ClientNotesAction = (id, name) =>
        {
            Selected = this[Page.Notes];
            PageOf<NotesViewModel>(Page.Notes).ShowClient(id, name);
        };
        go.OpenSettingsAction = section =>
        {
            Selected = this[Page.Settings];
            PageOf<SettingsViewModel>(Page.Settings).Open(section);
        };
        go.OpenFolderAction = (path, newTab) =>
        {
            if (newTab)
            {
                ((FilesViewModel)OpenInNewTab(this[Page.Files]).Page).GoTo(path);
                return;
            }
            Selected = this[Page.Files];
            PageOf<FilesViewModel>(Page.Files).GoTo(path);
        };
        go.OpenFolderInNewWindowAction = path =>
        {
            var host = OpenInNewTab(this[Page.Files]);
            ((FilesViewModel)host.Page).GoTo(path);
            PopOut(host);
        };
        go.MarkupPictureAction = path =>
        {
            Selected = this[Page.Markup];
            Markup.Show(new Avalonia.Media.Imaging.Bitmap(path));
        };
        go.ShowFileAction = path =>
        {
            Selected = this[Page.Files];
            PageOf<FilesViewModel>(Page.Files).Reveal(path);
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

    private SiteCheckViewModel BuildSiteCheck(AppServices services)
    {
        var page = new SiteCheckViewModel(services) { Announce = AnnounceSites };
        page.RefreshWatched();
        return page;
    }

    /// <summary>
    /// Every open copy of every page: each module's own, plus any opened again alongside it. The tabs show these.
    /// </summary>
    public ObservableCollection<PageHost> Open { get; }

    /// <summary>The page on screen: the copy shown is the one jumps land on.</summary>
    [ObservableProperty]
    public partial PageHost Current { get; set; }

    /// <summary>The copy of a module used last, which is where the sidebar and jumps take you.</summary>
    private PageHost LastUsed(Module module)
    {
        if (Current?.Module == module && Open.Contains(Current) && !IsElsewhere(Current))
            return Current;
        // A page popped out into its own window, or shown beside, stays there; the main side gets its own copy.
        if (Open.Where(h => h.Module == module && !IsElsewhere(h)).MaxBy(h => h.LastSeen) is { } here)
            return here;
        if (!IsElsewhere(module.Main))
            return module.Main;
        var copy = module.NewHost();
        Open.Add(copy);
        return copy;
    }

    /// <summary>A page, built now if it hasn't been: the copy on screen, or the one used last.</summary>
    private T PageOf<T>(Page key) where T : ViewModelBase => (T)LastUsed(this[key]).Page;

    /// <summary>Every built copy of these pages, for telling open pages something changed.</summary>
    private IEnumerable<T> Built<T>(params Page[] keys) => Open.Where(h => keys.Contains(h.Key) && h.IsBuilt).Select(h => h.Page).OfType<T>();

    /// <summary>Opens another copy of a page alongside the ones already open, and shows it.</summary>
    public PageHost OpenAnother(Page key)
    {
        var host = this[key].NewHost();
        Open.Add(host);
        Current = host;
        return host;
    }

    /// <summary>
    /// Closes an extra copy of a page and frees it. The module's own copy is never closed (only let go when
    /// idle), and a copy with open work stays until that work is saved or put away. Returns true when it closed.
    /// </summary>
    public bool Close(PageHost host, bool quietly = false)
    {
        if (host.IsMain || !Open.Contains(host) || (host.IsBuilt && host.Page is IHoldsWork { HoldsWork: true }))
            return false;
        var showing = Current == host;
        host.Release();
        Open.Remove(host);
        if (Tabs.Contains(host) && !showing)
            Tabs.Remove(host);
        if (showing && !quietly)
            Current = LastUsed(host.Module);
        OnPropertyChanged(nameof(ShowTabs));
        return true;
    }

    /// <summary>Lets go of pages nobody has looked at for a while, unless they hold open work.</summary>
    public int ReleaseIdle(TimeSpan idle)
    {
        var now = DateTimeOffset.Now;
        return Open.Count(h => h != Current && !IsElsewhere(h) && h.IsBuilt && now - h.LastSeen >= idle && h.Release());
    }

    public QuickSearchViewModel Search { get; }

    /// <summary>The running time-tracking timer, for the sidebar.</summary>
    public TimerClock Timer => services.Timer;

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
            case Webignite.Core.Search.HitKind.Note:
                services.Go.Note(hit.Note!.Id);
                break;
            case Webignite.Core.Search.HitKind.Login:
                services.Go.Client(hit.LoginClient!); // the login is on their page, with its copy buttons
                break;
            case Webignite.Core.Search.HitKind.File when Directory.Exists(hit.FilePath):
                services.Go.Folder(hit.FilePath!);
                break;
            case Webignite.Core.Search.HitKind.File:
                services.Go.ShowFile(hit.FilePath!);
                break;
            default:
                services.Go.To(Page.Renewals);
                break;
        }
    }

    // Each of these builds its page if it isn't built yet, so a jump from elsewhere always lands on it.
    public DocumentsViewModel Quotes => PageOf<DocumentsViewModel>(Page.Quotes);
    public DocumentsViewModel Invoices => PageOf<DocumentsViewModel>(Page.Invoices);
    public TodoViewModel Todo => PageOf<TodoViewModel>(Page.Todo);
    public OverviewViewModel Overview => PageOf<OverviewViewModel>(Page.Overview);
    public ClientsViewModel Clients => PageOf<ClientsViewModel>(Page.Clients);
    public FilesViewModel Files => PageOf<FilesViewModel>(Page.Files);
    public PdfViewModel Pdf => PageOf<PdfViewModel>(Page.Pdf);
    public NotesViewModel Notes => PageOf<NotesViewModel>(Page.Notes);
    public MailViewModel Mail => PageOf<MailViewModel>(Page.Mail);
    public ApprovalsViewModel Approvals => PageOf<ApprovalsViewModel>(Page.Approvals);
    public SiteCheckViewModel SiteCheck => PageOf<SiteCheckViewModel>(Page.SiteCheck);
    public MarkupViewModel Markup => PageOf<MarkupViewModel>(Page.Markup);
    public TimeViewModel Time => PageOf<TimeViewModel>(Page.Time);
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
            {
                Selected = module;
                return;
            }
            // A tool opens in a window of its own; the page on screen stays picked.
            if (value is ToolLink tool)
                services.Go.Tool(tool.Kind);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Runs when the window is closed: saves open quotes, invoices and the open client, sends a mail still in its
    /// undo wait and saves the mail being written. Returns what couldn't be kept, or null when nothing is lost.
    /// </summary>
    public async Task<string?> PrepareToCloseAsync()
    {
        // Only built pages can hold anything unsaved, and every open copy of a page is saved.
        foreach (var money in Built<MoneyViewModel>(Page.Money))
            money.SaveTyped();
        foreach (var documents in Built<DocumentsViewModel>(Page.Invoices, Page.Quotes))
            documents.Editor?.Keep();
        foreach (var approvals in Built<ApprovalsViewModel>(Page.Approvals))
            approvals.Editor?.Keep();
        foreach (var notes in Built<NotesViewModel>(Page.Notes))
            notes.Keep();
        string? problem = null;
        foreach (var clients in Built<ClientsViewModel>(Page.Clients))
        {
            if (clients.Detail is { } client && !client.Leave())
                problem ??= $"{client.Title} has something to fix before it can be saved.";
        }
        foreach (var mail in Built<MailViewModel>(Page.Mail))
            problem = await mail.PrepareToCloseAsync() ?? problem;
        return problem;
    }

    // The sidebar picks a module; the copy of it used last goes on screen.
    partial void OnSelectedChanged(Module value)
    {
        if (Current?.Module != value)
            Current = TabFor(value) ?? LastUsed(value);
    }

    partial void OnCurrentChanging(PageHost value)
    {
        leaving = Current;
        if (leaving is not null)
            leaving.LastSeen = DateTimeOffset.Now;
    }

    // These pages show things other pages change, so they look again whenever they're opened.
    partial void OnCurrentChanged(PageHost value)
    {
        KeepInTabs(value);
        leaving = null;
        value.LastSeen = DateTimeOffset.Now;
        if (Selected != value.Module)
            Selected = value.Module;
        services.CatchUp();
        if (mailModule is not null)
            UpdateMailBadge();
        if (value.Page is MailViewModel mail)
        {
            mail.Reload();
            mail.SyncAll().Forget("Checking mail");
        }
        else if (value.Page is IRefreshable page)
            page.Refresh();
    }
}
