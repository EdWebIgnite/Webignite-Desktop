using System.Diagnostics;
using System.Runtime.InteropServices;
using Webignite.Core.Writing;
using Webignite.Core.Backups;
using Webignite.Data;
using Webignite.Mail;
using Webignite.Services;
using Webignite.Xero.Auth;

using Webignite.Core.Documents;

namespace Webignite.Desktop;

/// <summary>
/// What the screens share: the local database, the settings, the Xero and mail connections, and the way
/// from one page to another. Rules live in Core; this only holds the pieces together.
/// </summary>
public sealed class AppServices : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private XeroSession? xero;

    public AppServices(LocalStore store)
    {
        Store = store;
        Documents = new DocumentService(store);
        Clients = new ClientService(store);
        Contacts = new ContactService(store);
        Contacts.Changed += () => addressBook = null;
        Settings = new AppSettings(store);
        Settings.XeroClientChanged += () => xero = null;
        Payments = new PaymentWatch(store, () => XeroApi);
        XeroClients = new XeroClientWatch(store, () => XeroApi);
        XeroDocuments = new XeroDocumentWatch(store, () => XeroApi);
        XeroBills = new XeroBillWatch(store, () => XeroApi);
        SiteWatch = new UptimeWatch(store.ListClients, () => Settings.Uptime, s => Settings.Uptime = s);
    }

    /// <summary>Reads payments from Xero every half hour.</summary>
    public PaymentWatch Payments { get; }

    /// <summary>Brings Xero's customers in as clients, on start and every few hours.</summary>
    public XeroClientWatch XeroClients { get; }

    /// <summary>Brings in quotes and invoices made in Xero itself, on start and every few hours.</summary>
    public XeroDocumentWatch XeroDocuments { get; }

    /// <summary>The bills in Xero still to approve or pay, read every few hours.</summary>
    public XeroBillWatch XeroBills { get; }

    /// <summary>Visits watched websites every 15 minutes.</summary>
    public UptimeWatch SiteWatch { get; }

    /// <summary>Domain, certificate and speed checks for the Site check page.</summary>
    public SiteChecker SiteChecker { get; init; } = SiteChecker.Default;

    public LocalStore Store { get; }

    /// <summary>Everything the person has set up, by area.</summary>
    public AppSettings Settings { get; }

    /// <summary>Opens pages, clients, documents and mail from anywhere. Wired by the main window.</summary>
    /// <summary>Quotes and invoices: pages read and change them here, never on the store directly.</summary>
    public DocumentService Documents { get; }

    /// <summary>Clients: pages read and change them here, never on the store directly.</summary>
    public ClientService Clients { get; }

    /// <summary>People in Contacts, linked to clients; what pages read and change them through.</summary>
    public ContactService Contacts { get; }

    /// <summary>Projects, headings and tasks, stamped with who changed them (<see cref="Me"/>) and when.</summary>
    public ProjectService Projects => projects ??= new ProjectService(Store, () => Me().Id);

    /// <summary>Signing in to the Webignite sync server; made the first time Settings, Account and sync needs it.</summary>
    public AccountClient Account => account ??= new AccountClient(Store, Secrets);
    private AccountClient? account;
    private ChatGptTunnelRunner? chatGpt;
    private ChatService? chat;

    /// <summary>Chat between the two of you; messages go with the sync.</summary>
    public ChatService Chat => chat ??= new ChatService(Store);

    /// <summary>The chat was read on this PC, so the sidebar's unread count can be updated.</summary>
    public Action? ChatRead { get; set; }

    /// <summary>A sync brought changes, perhaps new messages: the open chat and the unread count are brought up to date.</summary>
    public Action? ChatArrived { get; set; }

    /// <summary>OpenAI's tunnel, run in the background so ChatGPT can reach the connector on this PC.</summary>
    public ChatGptTunnelRunner ChatGpt => chatGpt ??= new ChatGptTunnelRunner(Settings, Secrets);
    private ProjectService? projects;

    /// <summary>Inbox: what your partner did on tasks you follow, mentions and likes.</summary>
    public InboxService Inbox => inbox ??= new InboxService(Store, () => Me().Id, Team);
    private InboxService? inbox;

    /// <summary>Shared projects going to and from the workspace on the sync server.</summary>
    public SyncService Sync => sync ??= new SyncService(Store, Account);
    private SyncService? sync;

    /// <summary>Signed in to a sync server on this PC; looked at without making the account client when there's none set up.</summary>
    public bool MaySync => (account is not null || Store.GetSetting<AccountSettings>("account") is { ServerAddress.Length: > 0 }) && Account.IsSignedIn;

    /// <summary>Set by the main window: tells the pages that are open which parts changed, e.g. projects after a sync.</summary>
    public Action<IReadOnlyList<string>>? AreasChanged { get; set; }

    /// <summary>
    /// Syncs shared projects now, off the screen's thread, and has the open pages read again when something came in.
    /// Problems are logged; the result says what happened for whoever asked.
    /// </summary>
    /// <summary>
    /// Signed in but not synced yet on this PC: Xero's clients, quotes and invoices wait to come in until the other
    /// PC's copies have, so the same one from Xero isn't kept twice.
    /// </summary>
    public bool WaitsForFirstSync => Account.IsSignedIn && Sync.State.LastSynced is null;

    /// <summary>Syncs now; by hand (Sync now), the licence is fetched again too, so a change to the plan shows at once.</summary>
    public async Task<SyncResult> SyncNowAsync(bool byHand = false)
    {
        var xeroApp = Settings.Xero.ClientId;
        var result = await Task.Run(() => Sync.SyncAsync());
        // The Xero app can arrive from the other PC: a sign-in for another app is started afresh.
        if (Settings.Xero.ClientId != xeroApp)
            xero = null;
        // The licence is fetched again twice a day while signed in, and at once from Sync now, so a renewal, a lapse or a
        // change of plan reaches this PC.
        if (!result.Skipped && result.Problem is null)
            await Task.Run(() => Account.RefreshLicenceAsync(byHand ? TimeSpan.Zero : TimeSpan.FromHours(12), DateTimeOffset.UtcNow));
        if (result.Changed > 0)
        {
            AreasChanged?.Invoke(["projects", "documents", "clients", "contacts", "chat", "settings"]);
            ChatArrived?.Invoke();
            AnnounceChat(await Task.Run(Chat.NewlyArrived));
            AnnounceInbox(await Task.Run(Inbox.NewlyArrived));
        }
        // Logged once when it starts going wrong, not every minute while the internet is down.
        if (result.Problem != lastSyncProblem && result.Problem is { } problem)
            Webignite.Data.AppLog.Write($"Syncing shared projects: {problem}");
        if (!result.Skipped)
            lastSyncProblem = result.Problem;
        return result;
    }

    private string? lastSyncProblem;

    // A note for each new message from the partner (the newest two, then a count), opening the chat when clicked.
    private void AnnounceChat(IReadOnlyList<Webignite.Core.Chat.ChatMessage> fresh)
    {
        foreach (var message in fresh.TakeLast(2))
            ShowNote?.Invoke($"Message from {Chat.NameOf(message.From)}", Webignite.Core.Chat.ChatRules.Preview(message), () => Go.Chat());
        if (fresh.Count > 2)
            ShowNote?.Invoke($"{fresh.Count - 2} more messages", "Open the chat to see them.", () => Go.Chat());
    }

    // A note for each new thing in Inbox (two at most, then one saying how many more), opening the task.
    private void AnnounceInbox(IReadOnlyList<Webignite.Core.Projects.InboxItem> fresh)
    {
        foreach (var item in fresh.Take(2))
            ShowNote?.Invoke(Webignite.Core.Projects.InboxRules.Headline(item), item.Snippet,
                () => { if (item.MessageId is null) Go.ProjectTask(item.TaskId); else Go.ProjectMessages(item.ProjectId); });
        if (fresh.Count > 2)
            ShowNote?.Invoke($"{fresh.Count - 2} more in Inbox", "Open Inbox in Planner to see them.", () => Go.To(Page.Inbox));
    }

    /// <summary>
    /// You and your partner, made the first time they're needed: "Me" and "Partner" until named in Settings.
    /// Ids stay the same, so names can change; the one exception is the partner's PC taking on the workspace
    /// owner's ids the first time it syncs.
    /// </summary>
    public Webignite.Core.Projects.TeamSettings Team()
    {
        var team = Settings.Team;
        if (team.Me is not null && team.Partner is not null)
            return team;
        var made = Webignite.Core.Projects.ProjectRules.Ensure(team, null);
        Settings.Team = made;
        return made;
    }

    /// <summary>The person using this PC, for "who changed it".</summary>
    public Webignite.Core.Projects.Teammate Me() => Team().Me!;

    /// <summary>Notebooks and notes: the Notes page, the client page and the quick note change them here.</summary>
    public NoteService Notes => notes ??= new NoteService(Store);
    private NoteService? notes;

    public Navigator Go { get; } = new();

    private MailDrafts? drafts;

    /// <summary>Prepares mail for the person to send: invoices, reminders, statements, onboarding and mark-ups.</summary>
    public MailDrafts Drafts => drafts ??= new MailDrafts(this);

    /// <summary>Background jobs, shown with a spinner in the sidebar.</summary>
    public WorkInProgress Work { get; } = new();

    /// <summary>Set by the main window: shows a note in the corner. The action runs when the note is clicked.</summary>
    public Action<string, string, Action?>? ShowNote { get; set; }

    /// <summary>After something is removed: a note that puts it back when clicked.</summary>
    /// <summary>
    /// Waves a mail off as needing no reply, from Needs you or the mail's right-click: it and older mail from that
    /// person stop waiting until they write again. Offers Undo. <see cref="RepliesChanged"/> tells Overview.
    /// </summary>
    public void NoReplyNeeded(Webignite.Core.Mail.MailSummary mail)
    {
        var before = Settings.NoReplyNeeded;
        Settings.NoReplyNeeded = Webignite.Core.Overview.MorningSummary.NoReply(before, mail, DateOnly.FromDateTime(DateTime.Today));
        RepliesChanged?.Invoke();
        OfferUndo($"No reply needed for {mail.From?.Display ?? "that mail"}", () =>
        {
            Settings.NoReplyNeeded = Webignite.Core.Overview.MorningSummary.ReplyNeeded(Settings.NoReplyNeeded, mail);
            RepliesChanged?.Invoke();
        });
    }

    public event Action? RepliesChanged;

    public void OfferUndo(string what, Action putBack) => ShowNote?.Invoke(what, "Click here to undo.", putBack);

    // Backups

    public const string BackupPasswordName = "Backup/password";

    /// <summary>The key that unlocks the database, needed inside each backup. Tests and screenshots pass their own.</summary>
    public Func<byte[]> VaultKeySource { get; init; } = () => VaultKey.Load(DataPaths.Root());

    public bool HasBackupPassword => Secrets.Read(BackupPasswordName) is { Length: > 0 };

    /// <summary>OneDrive when it's set up on this PC, so backups leave the machine; otherwise Documents.</summary>
    public static string DefaultBackupFolder()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        var parent = oneDrive is { Length: > 0 } && Directory.Exists(oneDrive)
            ? oneDrive
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(parent, "Webignite backups");
    }

    public string BackupFolder => Settings.Backups.Folder is { Length: > 0 } folder ? folder : DefaultBackupFolder();

    private bool backingUp;

    /// <summary>
    /// Makes a backup when one is due (or now, when asked). Copies the database on this thread, then encrypts
    /// in the background. Returns the problem, or null when it worked or wasn't needed.
    /// </summary>
    public async Task<string?> BackUpAsync(bool now = false) => (await TryBackUpAsync(now)).Problem;

    /// <summary>What a backup attempt did: made one, or why not (already running, no password, not due).</summary>
    public sealed record BackupOutcome(bool Made, string? Problem = null, string? Skipped = null);

    /// <summary>Backs up, saying what happened, so "Backed up." is only shown when a backup was made.</summary>
    public async Task<BackupOutcome> TryBackUpAsync(bool now = false)
    {
        if (backingUp)
            return new(false, Skipped: "A backup is already running. It finishes in a moment.");
        if (Secrets.Read(BackupPasswordName) is not { Length: > 0 } password)
            return new(false, Skipped: "Choose a backup password first.");
        var settings = Settings.Backups;
        var started = DateTimeOffset.Now;
        if (!now && !BackupRules.IsDue(settings, started))
            return new(false, Skipped: "No backup is due yet.");
        backingUp = true;
        using var working = Work.Start("Backing up");
        try
        {
            var snapshot = Backups.Snapshot(Store);
            var key = VaultKeySource();
            var folder = BackupFolder;
            var version = typeof(AppServices).Assembly.GetName().Version?.ToString(3);
            var file = await Task.Run(() => Backups.Seal(snapshot, key, password, folder, started, version));
            Settings.Backups = Settings.Backups with { LastBackup = started, LastFile = file, LastProblem = null, LastProblemAt = null };
            return new(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            var problem = e is UnauthorizedAccessException ? "Webignite can't write to the backup folder." : e.Message;
            Settings.Backups = Settings.Backups with { LastProblem = problem, LastProblemAt = DateTimeOffset.Now };
            return new(false, problem);
        }
        finally
        {
            backingUp = false;
        }
    }

    // Mail

    /// <summary>Mailbox passwords. Windows Credential Manager on Windows; memory only elsewhere (tests and screenshots).</summary>
    public ISecretStore Secrets { get; init; } = OperatingSystem.IsWindows() ? new WindowsSecretStore() : new MemorySecretStore();

    private MailService? mail;
    public MailService Mail => mail ??= new MailService(Store, Secrets);

    /// <summary>Files copied or cut in Files, waiting to be pasted, in any Files tab or window.</summary>
    public Files.FileClipboard FileClipboard { get; } = new();

    /// <summary>Set by the main window: takes the screenshot shortcut from Settings again. Returns why not, when it can't.</summary>
    public Func<string?>? ApplyScreenshotShortcut { get; set; }

    /// <summary>Set by the main window: takes the recording shortcut from Settings again. Returns why not, when it can't.</summary>
    public Func<string?>? ApplyRecordShortcut { get; set; }

    /// <summary>Set by the main window: takes the clipboard history shortcut from Settings again. Returns why not, when it can't.</summary>
    public Func<string?>? ApplyClipboardShortcut { get; set; }

    /// <summary>Set by the main window: takes the colour picker shortcut from Settings again. Returns why not, when it can't.</summary>
    public Func<string?>? ApplyColourShortcut { get; set; }

    /// <summary>A colour picked from the screen or a picture goes to the top of the recent colours.</summary>
    public void RememberColour(string hex)
    {
        Settings.RecentColours = Webignite.Core.Tools.Colours.Remember(Settings.RecentColours, hex);
        ColoursChanged?.Invoke();
    }

    /// <summary>Raised when the recent colours change, for the Colours tool if it's open.</summary>
    public event Action? ColoursChanged;

    /// <summary>
    /// Raised when the person's own pages change (added, renamed, removed, widgets moved), with the page that
    /// changed them, so the sidebar and any other open copy catch up and the one that did it doesn't reload.
    /// </summary>
    public event Action<object?>? MyPagesChanged;

    public void ChangedMyPages(object? by) => MyPagesChanged?.Invoke(by);

    /// <summary>The last things copied while the app is open, in memory only.</summary>
    public Files.ClipboardKeeper Clipboard { get; } = new();

    /// <summary>Settings asks the window to take up a new quick-note shortcut; returns a problem or null.</summary>
    public Func<string?>? ApplyQuickNoteShortcut { get; set; }

    /// <summary>A screen recording is under way, so closing the app should stop it properly first.</summary>
    public bool Recording { get; set; }

    private Files.FileTagBook? fileTags;
    private Files.CopyQueue? copyQueue;

    /// <summary>Big copies and moves from Files, one at a time, shared by every Files tab and window.</summary>
    public Files.CopyQueue CopyQueue => copyQueue ??= new Files.CopyQueue(() => FileTags);

    /// <summary>Tags on files and folders, read from the database the first time Files needs them.</summary>
    public Files.FileTagBook FileTags => fileTags ??= new Files.FileTagBook(Store, Settings);

    private TimerClock? timer;

    /// <summary>The time-tracking timer, shown in the sidebar and on Time.</summary>
    public TimerClock Timer => timer ??= new TimerClock(this);

    private MailCheck? mailCheck;

    /// <summary>The mail check that runs whether or not the Mail page is open.</summary>
    public MailCheck MailCheck => mailCheck ??= new MailCheck(this);

    private IReadOnlyList<Webignite.Core.Mail.Contact>? addressBook;
    private DateTime addressBookMade;

    /// <summary>
    /// Suggestions for To, Cc and Bcc. Built from Contacts, clients and cached mail, refreshed every ten minutes
    /// and as soon as Contacts change.
    /// </summary>
    public async Task<IReadOnlyList<Webignite.Core.Mail.Contact>> AddressBookAsync()
    {
        if (addressBook is { } book && DateTime.UtcNow - addressBookMade < TimeSpan.FromMinutes(10))
            return book;
        var accounts = Store.ListMailAccounts();
        var clients = Store.ListClients();
        var mail = await Task.Run(() => Store.FindMail(accounts.Select(a => a.Id), _ => true, 3000));
        addressBook = Webignite.Core.Mail.AddressBook.Build(clients, mail, accounts.Select(a => a.Address), DateTimeOffset.Now, Contacts.List());
        addressBookMade = DateTime.UtcNow;
        return addressBook;
    }

    // Xero

    public ITokenStore TokenStore { get; init; } = OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new MemoryTokenStore();

    public XeroSession? Xero =>
        xero ??= Settings.Xero.ClientId is { Length: > 0 } id ? new XeroSession(new XeroOptions { ClientId = id }, Http, TokenStore) : null;

    /// <summary>Xero's accounting API for the connected organisation, or null when Xero isn't connected.</summary>
    public Webignite.Xero.XeroApi? XeroApi =>
        Xero is { HasSavedSignIn: true } session && Settings.Xero.TenantId is { Length: > 0 } tenant ? new Webignite.Xero.XeroApi(session, Http, tenant) : null;

    /// <summary>Creates Xero drafts, saving each step to this PC as it goes.</summary>
    public Webignite.Xero.XeroDrafts? XeroDrafts =>
        XeroApi is { } api
            ? new Webignite.Xero.XeroDrafts(api, Documents.Put,
                // Only Webignite's own count as already linked; one brought in from Xero is still a duplicate to warn about.
                () => Documents.List().Where(d => d.Document.Origin != DocumentOrigin.Xero).Select(d => d.Xero.Id).OfType<string>().ToHashSet())
            : null;

    /// <summary>Approves, fetches Xero's PDF and marks documents sent, for emailing from Webignite's own mailbox.</summary>
    public Webignite.Xero.XeroCorrections? XeroCorrections => XeroApi is { } api ? new Webignite.Xero.XeroCorrections(api, (stored, change) => Documents.Change(stored.Id, change) ?? Documents.Put(change(stored))) : null;
    public Webignite.Xero.XeroSending? XeroSending => XeroApi is { } api ? new Webignite.Xero.XeroSending(api, (stored, change) => Documents.Change(stored.Id, change) ?? Documents.Put(change(stored))) : null;

    // The PC

    private DateOnly caughtUpOn;

    /// <summary>
    /// Does what the calendar calls for since the app last looked: drafts retainer and renewal invoices that are due,
    /// adds monthly expenses and renewal reminders. All of it stays on this PC and is safe to run any number of
    /// times. Runs once a day: at start, then on the first page change or timer tick after midnight.
    /// </summary>
    public void CatchUp()
    {
        if (caughtUpOn != DateOnly.FromDateTime(DateTime.Today))
            CatchUpNow();
    }

    /// <summary>The same, straight away: after a retainer, renewal or monthly cost was added or changed.</summary>
    public void CatchUpNow()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        caughtUpOn = today;
        var documents = Settings.Documents;
        Store.DraftRetainerInvoices(documents, today);
        Store.AddRecurringExpenses(today);
        Store.RaiseRenewals(documents, today);
    }

    /// <summary>Opens a link in the person's own default browser. Nothing is embedded in the app.</summary>
    public static Task OpenInBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>Opens a file with whatever Windows uses for it, e.g. a PDF reader.</summary>
    public static void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>The person's Downloads folder, where saved attachments go.</summary>
    public static string DownloadsFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private ISpelling? spelling;
    private bool spellingTried;

    /// <summary>
    /// The spell checker for writing, made the first time it's asked for; null when spell check is off or the PC
    /// has no checker. The snapshot tool puts in its own.
    /// </summary>
    public ISpelling? Spelling
    {
        get
        {
            if (!Settings.SpellCheck.On)
                return null;
            if (spelling is null && !spellingTried && OperatingSystem.IsWindows())
            {
                spellingTried = true;
                try
                {
                    spelling = WindowsSpelling.Open(Webignite.Core.Writing.Spelling.Wanted(Settings.SpellCheck));
                }
                catch (Exception e) when (e is COMException or InvalidCastException or TypeLoadException)
                {
                    AppLog.Write("Windows' spell checker couldn't be opened", e);
                }
            }
            return spelling;
        }
        set => spelling = value;
    }

    /// <summary>Spell check was switched on or off, or to another language: the next mail asks again.</summary>
    public void ResetSpelling()
    {
        (spelling as IDisposable)?.Dispose();
        spelling = null;
        spellingTried = false;
    }

    public void Dispose()
    {
        (spelling as IDisposable)?.Dispose();
        // Off the screen's thread: closing the connections continues on whatever thread it started on, so
        // waiting for it here, on that same thread, would only ever time out.
        if (mail is { } open)
            Task.Run(() => open.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));
        xero?.Dispose();
        chatGpt?.Dispose();
        sync?.Dispose();
        account?.Dispose();
        Store.Dispose();
    }
}
