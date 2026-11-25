using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppServices services;

    // Each part with state of its own is its own class; this holds them and the screen binds to them by name.
    public MainWelcomePart WelcomePart { get; }
    public MainDockBarPart DockBarPart { get; }
    public MainMenuPart MenuPart { get; }

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
            services.Notify(payment.Title, payment.Text, () => Selected = this[Page.Invoices]);
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
        foreach (var page in Built<IRefreshable>(Page.Calendar, Page.Diary, Page.Overview))
            page.Refresh();
    }

    /// <summary>
    /// Claude or ChatGPT changed something, or a sync brought your partner's changes in: the pages already built
    /// that show it read it again. Projects tell their own listeners (sidebar, Home, widgets, the Projects pages);
    /// the rest are refreshed here.
    /// </summary>
    private void ShowAssistantChanges(IReadOnlyList<string> areas)
    {
        var pages = new HashSet<Page>();
        // Settings lists what they changed.
        pages.Add(Page.Settings);
        foreach (var area in areas)
        {
            if (area == "projects")
            {
                services.Projects.ChangedElsewhere();
                UpdateInboxBadgeAsync().Forget("Counting what's new in Inbox");
            }
            pages.UnionWith(area switch
            {
                "projects" => [Page.Calendar, Page.Diary, Page.Overview, Page.MyPages, Page.Inbox],
                "todos" => [Page.Calendar, Page.Diary, Page.Overview, Page.Home, Page.Projects, Page.MyPages],
                "clients" => [Page.Clients, Page.Overview, Page.Retainers],
                "documents" => [Page.Quotes, Page.Invoices, Page.Money, Page.Overview],
                "contacts" => [Page.Contacts],
                "notes" => [Page.Notes],
                "calendar" => [Page.Calendar, Page.Diary, Page.Overview, Page.Home],
                "time" => [Page.Time, Page.Overview],
                "renewals" => [Page.Renewals, Page.Diary, Page.Overview],
                _ => [],
            });
        }
        // A page with work open (a note being written, a client being edited) is left alone so nothing typed is lost.
        foreach (var page in Built<IRefreshable>([.. pages]).Where(p => p is not IHoldsWork { HoldsWork: true }))
            page.Refresh();
    }

    /// <summary>Brings Xero's customers in as clients and tells the Clients pages that are open.</summary>
    private async Task CheckXeroClientsAsync()
    {
        if (services.WaitsForFirstSync)
            return;
        var news = await services.Work.Run("Bringing clients in from Xero", () => services.XeroClients.RunAsync());
        if (news.Added + news.Filled == 0)
            return;
        foreach (var page in Built<ClientsViewModel>(Page.Clients))
            page.Refresh();
        if (news.Added > 0)
            services.ShowNote?.Invoke("Clients from Xero", news.Summary + ".", () => Selected = this[Page.Clients]);
    }

    /// <summary>Brings in quotes and invoices made in Xero itself and has the pages that show money read again.</summary>
    private async Task CheckXeroDocumentsAsync()
    {
        if (services.WaitsForFirstSync)
            return;
        var news = await services.Work.Run("Bringing in quotes and invoices from Xero", () => services.XeroDocuments.RunAsync());
        if (news.Problem is { } problem)
            Webignite.Data.AppLog.Write(problem);
        if (news.Added + news.Refreshed == 0)
            return;
        RefreshMoneyPages();
        if (news.Added > 0)
            services.ShowNote?.Invoke("From Xero", news.Summary, () => Selected = this[Page.Invoices]);
    }

    private async Task CheckXeroBillsAsync()
    {
        if (await services.Work.Run("Reading bills from Xero", () => services.XeroBills.RunAsync()) is { } problem)
        {
            Webignite.Data.AppLog.Write(problem);
            return;
        }
        foreach (var page in Built<IRefreshable>([Page.Overview, Page.Money]).Where(p => p is not IHoldsWork { HoldsWork: true }))
            page.Refresh();
    }

    /// <summary>The pages that list or count quotes and invoices, read again.</summary>
    internal void RefreshMoneyPages()
    {
        foreach (var page in Built<IRefreshable>([Page.Quotes, Page.Invoices, Page.Overview, Page.Money, Page.Clients])
                     .Where(p => p is not IHoldsWork { HoldsWork: true }))
            page.Refresh();
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
            services.Notify(alert.Title, alert.Text, () => Selected = this[Page.SiteCheck]);
        if (alerts.Count > 3)
            services.ShowNote?.Invoke($"{alerts.Count - 3} more sites changed", "Open Site check to see them.", () => Selected = this[Page.SiteCheck]);
        if (alerts.Any(a => a.Problem) && WindowIsActive?.Invoke() != true)
            FlashTaskbar?.Invoke();
    }
    private bool checkingMail;
    private readonly Module mailModule;
    private readonly Module inboxModule;

    /// <summary>Unread in Inbox, beside it in the Planner sidebar. Worked out off the screen's thread.</summary>
    private async Task UpdateInboxBadgeAsync() => inboxModule.Badge = await Task.Run(services.Inbox.Unread);

    /// <summary>Set by the main window: whether it's the window in front, so a note isn't shown for mail already on screen.</summary>
    public Func<bool>? WindowIsActive { get; set; }

    /// <summary>Set by the main window: makes the taskbar button flash when new mail comes in while the app is behind.</summary>
    public Action? FlashTaskbar { get; set; }

    /// <summary>Unread mail across all inboxes, shown beside Mail in the sidebar.</summary>
    public void UpdateMailBadge() =>
        mailModule.Badge = services.Store.ListMailAccounts().Sum(a =>
            services.Store.ListMailFolders(a.Id).FirstOrDefault(f => f.Role == Webignite.Core.Mail.FolderRole.Inbox)?.Unread ?? 0);

    private bool watchingMail;

    /// <summary>
    /// Has the mail server say when mail lands (IMAP IDLE), so it shows within seconds; the check every few minutes
    /// stays. Started by the app itself, not by tests or the screenshot tool, which have no real mail server.
    /// </summary>
    public void WatchMail()
    {
        watchingMail = true;
        services.MailWatch.NewMail = _ => Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckMailAsync().Forget("Checking mail"));
        services.MailWatch.Keep(services.Store.ListMailAccounts());
    }

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
            // A mailbox added or its password saved since: watched from now on.
            if (watchingMail)
                services.MailWatch.Keep(services.Store.ListMailAccounts());

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
                services.Notify(title, text, () => services.Go.Mail(first));
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
        WelcomePart = new(services, this);
        DockBarPart = new(services, this);
        MenuPart = new(services, this);
        services.CatchUp();
        Updates.BeforeRestart = () =>
        {
            services.ChatGpt.Dispose();
            if (OperatingSystem.IsWindows())
            {
                ChatGptTunnelRunner.StopStrays();
                AssistantSetup.StopConnectors();
            }
        };
        Updates.CheckAsync().Forget("Checking for an update");
        // The sidebar, in groups. Headings sit between the groups; the pages keep the order shown here.
        // Each page is built the first time it is opened, so starting the app builds Overview and nothing else.
        object[] nav =
        [
            new Module(Page.Overview, "Overview", () => BuildOverview(services), "Overview"),
            new Module(Page.Mail, "Mail", () => new MailViewModel(services), "Mail", fullBleed: true),
            // The pages behind the tabs across the top: everything in an area as tiles. The tabs open them; they
            // aren't on the sidebar themselves, which is the shortlist of what the person pinned.
            new Module(Page.Business, "Business", () => new AreaViewModel(services, AppArea.Business), "Overview") { IsArea = true },
            new Module(Page.Computer, "Computer", () => new AreaViewModel(services, AppArea.Computer), "Folder") { IsArea = true },
            new Module(Page.Tools, "Tools", () => new AreaViewModel(services, AppArea.Tools), "Tools") { IsArea = true },
            plannerSection,
            new Module(Page.Home, "Home", () => new PlannerHomeViewModel(services), "Home") { InPlanner = true },
            new Module(Page.Inbox, "Inbox", () => new InboxViewModel(services), "Inbox") { InPlanner = true },
            new NavGroup("my-pages", "MY PAGES") { Icon = "Panels" },
            new Module(Page.MyPages, "All my pages", () => new MyPagesViewModel(services), "Tiles"),
            new NavGroup("client-work", "CLIENT WORK") { Icon = "Clients" },
            new Module(Page.Clients, "Clients", () => new ClientsViewModel(services), "Clients", fullBleed: true),
            new Module(Page.Contacts, "Contacts", () => new ContactsViewModel(services), "Contacts", fullBleed: true),
            // Billing is one entry: quotes, invoices, money and what recurs are its tabs. It has no page of its own;
            // opening it opens the tab used last. The four pages are as they were, only not on the menus themselves.
            new Module(Page.Billing, "Billing", () => new TabsOnlyViewModel(), "Invoices") { Tabs = BillingTabPages },
            new Module(Page.Quotes, "Quotes", () => new DocumentsViewModel(DocumentKind.Quote, services), "Quotes") { Under = Page.Billing },
            new Module(Page.Invoices, "Invoices", () => new DocumentsViewModel(DocumentKind.Invoice, services), "Invoices") { Under = Page.Billing },
            new Module(Page.Money, "Money", () => new MoneyViewModel(services), "Money") { Under = Page.Billing },
            new Module(Page.Retainers, "Recurring", () => new RetainersViewModel(services), "Retainers") { Under = Page.Billing },
            new Module(Page.Studio, "Studio", () => new StudioViewModel(services), "Studio", fullBleed: true),
            new Module(Page.Projects, "Projects", () => new ProjectsViewModel(services), "Projects", fullBleed: true) { InPlanner = true },
            new Module(Page.Calendar, "Calendar", () => new CalendarViewModel(services), "Calendar", fullBleed: true) { InPlanner = true },
            new Module(Page.Diary, "Diary", () => new DiaryViewModel(services), "Diary") { InPlanner = true },
            new Module(Page.Time, "Time", () => new TimeViewModel(services), "Time") { InPlanner = true },
            new NavGroup("computer", "COMPUTER") { Icon = "Tools" },
            new Module(Page.Files, "Files", () => new FilesViewModel(services), "Folder", fullBleed: true),
            new Module(Page.Pdf, "PDF tools", () => new PdfViewModel(services), "Quotes", fullBleed: true),
            new Module(Page.Notes, "Notes", () => new NotesViewModel(services), "Notes", fullBleed: true),
            // The small tools live on their area's page; the ones pinned there show here too, under their heading.
            ToolCatalogue.Link(ToolKind.Compare),
            ToolCatalogue.Link(ToolKind.Duplicates),
            ToolCatalogue.Link(ToolKind.Calculator),
            ToolCatalogue.Link(ToolKind.Passwords),
            ToolCatalogue.Link(ToolKind.Colours),
            ToolCatalogue.Link(ToolKind.Snippets),
            ToolCatalogue.Link(ToolKind.ScreenText),
            new NavGroup("web-tools", "WEB TOOLS") { Icon = "SiteCheck" },
            new Module(Page.SiteCheck, "Site check", () => BuildSiteCheck(services), "SiteCheck"),
            new Module(Page.Crawler, "Site crawler", () => new CrawlerViewModel(services), "Crawler"),
            new Module(Page.Markup, "Mark-up", () => new MarkupViewModel(services), "Markup"),
            ToolCatalogue.Link(ToolKind.Images),
            ToolCatalogue.Link(ToolKind.Logos),
            ToolCatalogue.Link(ToolKind.Social),
            ToolCatalogue.Link(ToolKind.Qr),
            ToolCatalogue.Link(ToolKind.WebHelpers),
            ToolCatalogue.Link(ToolKind.Dns),
            ToolCatalogue.Link(ToolKind.Browser),
            ToolCatalogue.Link(ToolKind.PageCapture),
            ToolCatalogue.Link(ToolKind.Ruler),
            new NavGroup("end", ""),
            new Module(Page.Settings, "Settings", () => new SettingsViewModel(services), "Settings", readingWidth: true),
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
        // The areas' own pages are opened by the tabs only: they are never on the menus, nor among what can be put there.
        // Nor are the pages that are tabs of another (Billing's): their entry is the one on the menus.
        // When the rest moves to the areas' pages, someone who has made pages of their own keeps them on the sidebar,
        // and someone who uses the dock keeps what is on it.
        IReadOnlyList<string> Keep(IReadOnlyList<string> defaults)
        {
            var appearance = services.Settings.Appearance;
            return
            [
                .. services.Settings.MyPages.Count > 0 ? [SidebarViewModel.KeyOf(Page.MyPages)] : Array.Empty<string>(),
                .. appearance.Menu == Webignite.Core.Layout.MenuKind.Dock ? Webignite.Core.Layout.Sidebar.OnDock(Webignite.Core.Layout.DockPages.Moved(appearance.Dock ?? Webignite.Core.Layout.DockPages.Usual), defaults) : [],
            ];
        }
        Sidebar = new SidebarViewModel(services.Settings, [.. nav.Where(item => item is not Module { IsArea: true } and not Module { Under: not null })], ToolCatalogue.OffToStart, Keep) { EditAsked = () => MenuPart.EditMenuCommand.Execute(null) };
        services.Go.IsOnMenusAsked = Sidebar.IsOnMenus;
        services.Go.ShowOnMenusAction = Sidebar.ShowOnMenus;
        AreaTabs = [.. Enum.GetValues<AppArea>().Select(area => new AreaTab(area, a => GoTo(ToolCatalogue.AreaPage(a))))];
        BillingTabs = [.. BillingTabPages.Select(page => new PageTab(this[page], module => Selected = module))];
        Sidebar.LayoutChanged += MenuPart.MenusChanged;
        services.Go.EditMenuAction = () => MenuPart.EditMenuCommand.Execute(null);
        Open = [.. Modules.Select(m => m.Main)];
        Current = Modules[0].Main;
        Selected = Modules[0];
        mailModule = this[Page.Mail];
        inboxModule = this[Page.Inbox];
        services.Inbox.Changed += () => UpdateInboxBadgeAsync().Forget("Counting what's new in Inbox");
        services.MailCountsChanged = () => { if (mailModule is not null) UpdateMailBadge(); };
        Sidebar.WorkHeading.AddCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => services.Go.NewProject());
        // When the sidebar swaps lists, the list now on screen marks the page.
        Sidebar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.IsPlanner))
                OnPropertyChanged(nameof(SelectedNav));
        };
        ListProjects();
        services.Projects.Changed += _ => ListProjects();
        ListMyPages();
        services.MyPagesChanged += _ => ListMyPages();
        Jobs = new BackgroundJobs(services)
        {
            CheckMail = CheckMailAsync,
            CheckPayments = () => CheckPaymentsAsync(),
            CheckSites = CheckSitesAsync,
            CheckXeroClients = CheckXeroClientsAsync,
            CheckXeroDocuments = CheckXeroDocumentsAsync,
            CheckXeroBills = CheckXeroBillsAsync,
            SyncProjects = () => services.SyncNowAsync(),
            ChatOpen = () => services.ChatOpen,
            EveryMinute = () =>
            {
                ReleaseIdle(PageIdle);
                Remind(DateTime.Now);
                // A new version is looked for every hour while Webignite stays open, not only at start.
                if (Updates.CheckDue(DateTimeOffset.Now))
                    Updates.CheckAsync().Forget("Checking for an update");
                // ChatGPT's tunnel, when it's connected, runs while Webignite is open and starts again if it stops.
                if (OperatingSystem.IsWindows())
                    services.ChatGpt.KeepRunning();
            },
            AssistantChanged = ShowAssistantChanges,
        };
        services.AreasChanged = ShowAssistantChanges;
        Jobs.Start();
        if (OperatingSystem.IsWindows() && services.Settings.ChatGptTunnel.On)
            services.ChatGpt.KeepRunning();
        WelcomePart.Welcome();
        UpdateInboxBadgeAsync().Forget("Counting what's new in Inbox");

        // How other pages get here. Each one lands on the right page first, then opens the thing.
        var go = services.Go;
        go.OpenPage = GoTo;
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
        go.ComposeMailAction = (mail, note, afterSent, beforeSend) =>
        {
            Selected = mailModule;
            if (!Mail.Compose(mail, afterSent: afterSent, beforeSend: beforeSend))
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
        go.OpenPersonAction = (id, clientId) =>
        {
            Selected = this[Page.Contacts];
            var contacts = PageOf<ContactsViewModel>(Page.Contacts);
            if (id is not null)
                contacts.OpenPerson(id);
            else
                contacts.New(clientId);
        };
        go.OpenProjectTaskAction = id =>
        {
            Selected = this[Page.Projects];
            PageOf<ProjectsViewModel>(Page.Projects).OpenTaskById(id);
        };
        go.SiteCheckAction = address =>
        {
            Selected = this[Page.SiteCheck];
            var check = PageOf<SiteCheckViewModel>(Page.SiteCheck);
            check.Address = address;
            if (check.CheckDomainCommand.CanExecute(null))
                check.CheckDomainCommand.ExecuteAsync(null).Forget("Checking a site from the browser", tell: true);
        };
        go.OpenDiaryAction = day =>
        {
            Selected = this[Page.Diary];
            PageOf<DiaryViewModel>(Page.Diary).Show(day);
        };
        go.OpenProjectAction = id =>
        {
            Selected = this[Page.Projects];
            PageOf<ProjectsViewModel>(Page.Projects).OpenProject(id);
        };
        go.OpenProjectMessagesAction = id =>
        {
            Selected = this[Page.Projects];
            var projects = PageOf<ProjectsViewModel>(Page.Projects);
            projects.OpenProject(id);
            projects.MessagesViewCommand.Execute(null);
        };
        go.MyTasksAction = () =>
        {
            Selected = this[Page.Projects];
            PageOf<ProjectsViewModel>(Page.Projects).ShowMine();
        };
        // Create project opens the template gallery, with a blank project as its first choice, as in Asana.
        go.NewProjectAction = () =>
        {
            Selected = this[Page.Projects];
            PageOf<ProjectsViewModel>(Page.Projects).ShowTemplates();
        };
        go.ClientNotesAction = (id, name) =>
        {
            Selected = this[Page.Notes];
            PageOf<NotesViewModel>(Page.Notes).ShowClient(id, name);
        };
        go.ProjectNotesAction = (id, name) =>
        {
            Selected = this[Page.Notes];
            PageOf<NotesViewModel>(Page.Notes).ShowProject(id, name);
        };
        go.SetupGuideAction = () => WelcomePart.StartGuide();
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
        // The menu's kind and the dock's pages are part of the look; whichever is picked follows it at once.
        services.Settings.AppearanceChanged += _ => MenuPart.MenuKindChanged();
        MenuPart.FillDock();
        DockBarPart.FillBar();
    }

    /// <summary>The sidebar entry for a page.</summary>
    /// <summary>
    /// Opens a page. One that was folded into another opens there on its own view: Domains and hosting is a tab
    /// on Recurring, so going to it shows that tab, and going to Retainers shows the other.
    /// </summary>
    public void GoTo(Page page)
    {
        Selected = this[page];
        if (page is Page.Renewals or Page.Retainers && Current.Page is RetainersViewModel recurring && recurring.IsListing)
            recurring.IsRenewals = page == Page.Renewals;
    }

    /// <summary>
    /// The page to open for a name. An entry that is several pages in one (Billing) has no page of its own, so its
    /// name gives the tab used last.
    /// </summary>
    public Module this[Page page] => Lands(Modules.Single(m => m.Key == NowIn(page)));

    /// <summary>Where opening an entry lands: the page itself, or for one that is several pages in one, the tab used last.</summary>
    public Module Lands(Module entry) => entry.Tabs is null ? entry : Modules.Single(m => m.Key == lastTab[entry.Key]);

    /// <summary>Where a page lives now: one that was folded into another (Week into Calendar) opens that one, so a tab or layout saved before still works.</summary>
    public static Page NowIn(Page page) => page switch
    {
        Page.Planner => Page.Calendar,
        Page.Reports => Page.Money,
        Page.Renewals => Page.Retainers,
        Page.Approvals => Page.Overview,
        _ => page,
    };

    private OverviewViewModel BuildOverview(AppServices services)
    {
        var page = new OverviewViewModel(services) { CheckPayments = () => CheckPaymentsAsync(byHand: true) };
        // What waits to be approved shows as a count on Billing, and on its Quotes and Invoices tabs.
        page.Approvals.Counted = (quotes, invoices) =>
        {
            this[Page.Quotes].Badge = quotes;
            this[Page.Invoices].Badge = invoices;
            Modules.First(m => m.Key == Page.Billing).Badge = quotes + invoices;
            foreach (var tab in BillingTabs)
                tab.Title = Webignite.Core.Layout.PageTabs.Title(tab.Module.Title, tab.Module.Badge);
        };
        page.Approvals.Refresh();
        return page;
    }

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
    internal T PageOf<T>(Page key) where T : ViewModelBase => (T)LastUsed(this[key]).Page;

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
                services.Go.MyTasks();
                break;
            case Webignite.Core.Search.HitKind.Person:
                services.Go.Person(hit.Person!.Id);
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
    public OverviewViewModel Overview => PageOf<OverviewViewModel>(Page.Overview);
    public ClientsViewModel Clients => PageOf<ClientsViewModel>(Page.Clients);
    public FilesViewModel Files => PageOf<FilesViewModel>(Page.Files);
    public PdfViewModel Pdf => PageOf<PdfViewModel>(Page.Pdf);
    public NotesViewModel Notes => PageOf<NotesViewModel>(Page.Notes);
    public MailViewModel Mail => PageOf<MailViewModel>(Page.Mail);
    public ApprovalsViewModel Approvals => Overview.Approvals;
    public SiteCheckViewModel SiteCheck => PageOf<SiteCheckViewModel>(Page.SiteCheck);
    public MarkupViewModel Markup => PageOf<MarkupViewModel>(Page.Markup);
    public TimeViewModel Time => PageOf<TimeViewModel>(Page.Time);
    public IReadOnlyList<Module> Modules { get; }

    /// <summary>Messages from your partner not read yet, for the Chat button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChatUnread))]
    public partial int ChatUnread { get; private set; }

    public bool HasChatUnread => ChatUnread > 0;

    public void UpdateChatUnread() => ChatUnread = services.Chat.Unread;

    /// <summary>My partner's chat status, as the dot on the Chat button.</summary>
    [ObservableProperty] public partial Webignite.Core.Chat.PresenceState ChatPartnerShown { get; private set; } = Webignite.Core.Chat.PresenceState.Offline;

    public string ChatPartnerTip => $"Chat with {services.Chat.PartnerName}: {Webignite.Core.Chat.PresenceRules.Word(ChatPartnerShown).ToLowerInvariant()}";

    partial void OnChatPartnerShownChanged(Webignite.Core.Chat.PresenceState value) => OnPropertyChanged(nameof(ChatPartnerTip));

    public void UpdateChatPresence() => ChatPartnerShown = services.Chat.PartnerShown;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenChat() => services.Go.ToggleChat();

    /// <summary>The sidebar itself: hidden or showing, and which groups are folded.</summary>
    public SidebarViewModel Sidebar { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNav), nameof(SelectedIcon))]
    public partial Module Selected { get; set; }

    /// <summary>
    /// The icon strip's selection: the page, or the icon of the group it's in, as a group is one icon there. Picking a
    /// group's icon opens nothing by itself (its pages open beside it), so the pick goes back to the page on screen.
    /// </summary>
    public object? SelectedIcon
    {
        get => !Sidebar.IsPlanner && OnMenusFor(Selected).Group is { CanFold: true } group && Sidebar.IconPages.Contains(group) ? group : OnMenusFor(Selected);
        set
        {
            if (value is not NavGroup)
                SelectedNav = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The sidebar's selection. Headings can't be picked, so anything that isn't a page is ignored.</summary>
    public object? SelectedNav
    {
        get => (object?)ShownProjectLink() ?? (object?)ShownMyPageLink()
            ?? (Selected is { InPlanner: true } && !Sidebar.IsPlanner ? plannerSection : OnMenusFor(Selected));
        set
        {
            // A list that doesn't hold the page on screen clears its pick; that's not a choice.
            if (value is null)
                return;
            if (value is Module module)
            {
                // Projects, clicked while a project, My tasks or Templates is open there, goes back to all projects.
                if (module.Key == Page.Projects && Selected == module && Current.Page is ProjectsViewModel { Showing: not null } projects)
                    projects.ShowAll();
                Selected = module;
                return;
            }
            if (value is ProjectLink link)
                OpenProjectLink(link);
            if (value is MyPageLink pageLink)
                OpenMyPageLink(pageLink);
            if (value is MyPageGroupLink pageGroup)
                OpenMyPageGroup(pageGroup);
            // The Planner entry opens its sidebar, and Home unless a Planner page is on screen already (back to the main
            // sidebar and in again); the arrow at the top goes back to the main sidebar.
            if (value is SectionLink)
            {
                if (!Selected.InPlanner)
                    Selected = this[Page.Home];
                Sidebar.EnterPlanner();
            }
            if (value is SidebarBack)
                Sidebar.LeavePlanner();
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
    public async Task<string?> PrepareToCloseAsync() => await FinishBeforeCloseAsync(KeepOpenWork());

    /// <summary>
    /// The first half of closing, done at once on this PC: everything typed and not yet saved is kept. Returns what
    /// couldn't be saved and needs the person, or null.
    /// </summary>
    public string? KeepOpenWork()
    {
        // Only built pages can hold anything unsaved, and every open copy of a page is saved.
        foreach (var money in Built<MoneyViewModel>(Page.Money))
            money.SaveTyped();
        foreach (var documents in Built<DocumentsViewModel>(Page.Invoices, Page.Quotes))
            documents.Editor?.Keep();
        foreach (var notes in Built<NotesViewModel>(Page.Notes))
            notes.Keep();
        foreach (var projects in Built<ProjectsViewModel>(Page.Projects))
            projects.Keep();
        foreach (var pages in Built<MyPagesViewModel>(Page.MyPages))
            pages.Keep();
        // A note typed into a widget on Overview.
        foreach (var overview in Built<OverviewViewModel>(Page.Overview))
            overview.Board?.Keep();
        foreach (var home in Built<PlannerHomeViewModel>(Page.Home))
            home.KeepNotepad();
        foreach (var diary in Built<DiaryViewModel>(Page.Diary))
            diary.Keep();
        string? problem = null;
        foreach (var contacts in Built<ContactsViewModel>(Page.Contacts))
        {
            if (!contacts.Keep())
                problem ??= $"{contacts.TabTitle} has something to fix before it can be saved.";
        }
        foreach (var clients in Built<ClientsViewModel>(Page.Clients))
        {
            if (clients.Detail is { } client && !client.Leave())
                problem ??= $"{client.Title} has something to fix before it can be saved.";
        }
        return problem;
    }

    /// <summary>
    /// The second half of closing, which can take a few seconds and needs nothing on screen: a mail in its undo
    /// wait goes, the mail being written is saved to Drafts, and the last changes go to your partner.
    /// </summary>
    public async Task<string?> FinishBeforeCloseAsync(string? problem)
    {
        foreach (var mail in Built<MailViewModel>(Page.Mail))
            problem = await mail.SendingPart.PrepareToCloseAsync() ?? problem;
        // What changed since the last full sync goes to your partner now, waiting a few seconds at most.
        if (problem is null && services.MaySync)
            await Task.WhenAny(services.SyncNowAsync(full: true), Task.Delay(TimeSpan.FromSeconds(5)));
        return problem;
    }

    // The sidebar picks a module; the copy of it used last goes on screen.
    /// <summary>
    /// The tabs across the top of the window: Mail, Business, Planner, Computer, Tools. Mail opens Mail and Planner its
    /// Home; the others open their area's page of tiles. The tab of the area the page on screen belongs to is lit.
    /// </summary>
    public IReadOnlyList<AreaTab> AreaTabs { get; private set; } = [];

    private void ShowArea(Module? on)
    {
        var area = on is null ? null : ToolCatalogue.AreaOf(on.Key, on.InPlanner);
        foreach (var tab in AreaTabs)
            tab.IsOn = tab.Area == area;
    }

    /// <summary>
    /// The tabs across the top of Billing's pages: Quotes, Invoices, Money and Recurring, the one on screen lit, with
    /// how many wait to be approved after Quotes and Invoices.
    /// </summary>
    public IReadOnlyList<PageTab> BillingTabs { get; private set; } = [];

    /// <summary>One of Billing's pages is on screen, so its tabs show above it.</summary>
    public bool ShowBillingTabs => Selected?.Under == Page.Billing;

    private static readonly Page[] BillingTabPages = [Page.Quotes, Page.Invoices, Page.Money, Page.Retainers];

    // The tab each several-pages-in-one entry was last on, so opening the entry goes back there. Invoices to begin with.
    private readonly Dictionary<Page, Page> lastTab = new() { [Page.Billing] = Page.Invoices };

    /// <summary>The entry on the menus that stands for a page: the page itself, or the entry it is a tab of (Billing, for Invoices).</summary>
    public Module OnMenusFor(Module module) => module.Under is { } lead ? Modules.First(m => m.Key == lead) : module;

    /// <summary>Whether a menu entry is the one for the page on screen: the page, or the entry the page is a tab of.</summary>
    public bool IsOnScreen(Module entry) => Selected == entry || Selected?.Under == entry.Key;

    partial void OnSelectedChanged(Module value)
    {
        // An entry that is several pages in one (Billing) has no page of its own: it opens the tab used last.
        if (value.Tabs is not null)
        {
            Selected = Lands(value);
            return;
        }
        if (value.Under is { } lead)
            lastTab[lead] = value.Key;
        foreach (var tab in BillingTabs)
            tab.IsOn = tab.Module == value;
        OnPropertyChanged(nameof(ShowBillingTabs));
        ShowArea(value);
        if (Current?.Module != value)
            Current = TabFor(value) ?? LastUsed(value);
        // A Planner page brings the Planner sidebar up; any other page puts the main one back.
        if (value.InPlanner)
            Sidebar?.EnterPlanner();
        else
            Sidebar?.LeavePlanner();
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
        MenuPart.FillDock();
        WatchPanes(value);
        WatchProjects(value);
        WatchMyPages(value);
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
