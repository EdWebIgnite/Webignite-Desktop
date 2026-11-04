using System.Diagnostics;
using Webignite.Core.Backups;
using Webignite.Data;
using Webignite.Mail;
using Webignite.Services;
using Webignite.Xero.Auth;

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
        Settings = new AppSettings(store);
        Settings.XeroClientChanged += () => xero = null;
        Payments = new PaymentWatch(store, () => XeroApi);
        SiteWatch = new UptimeWatch(store.ListClients, () => Settings.Uptime, s => Settings.Uptime = s);
    }

    /// <summary>Reads payments from Xero every half hour.</summary>
    public PaymentWatch Payments { get; }

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

    public Navigator Go { get; } = new();

    private MailDrafts? drafts;

    /// <summary>Prepares mail for the person to send: invoices, reminders, statements, onboarding and mark-ups.</summary>
    public MailDrafts Drafts => drafts ??= new MailDrafts(this);

    /// <summary>Background jobs, shown with a spinner in the sidebar.</summary>
    public WorkInProgress Work { get; } = new();

    /// <summary>Set by the main window: shows a note in the corner. The action runs when the note is clicked.</summary>
    public Action<string, string, Action?>? ShowNote { get; set; }

    /// <summary>After something is removed: a note that puts it back when clicked.</summary>
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

    /// <summary>Suggestions for To, Cc and Bcc. Built from clients and cached mail, and refreshed every ten minutes.</summary>
    public async Task<IReadOnlyList<Webignite.Core.Mail.Contact>> AddressBookAsync()
    {
        if (addressBook is { } book && DateTime.UtcNow - addressBookMade < TimeSpan.FromMinutes(10))
            return book;
        var accounts = Store.ListMailAccounts();
        var clients = Store.ListClients();
        var mail = await Task.Run(() => Store.FindMail(accounts.Select(a => a.Id), _ => true, 3000));
        addressBook = Webignite.Core.Mail.AddressBook.Build(clients, mail, accounts.Select(a => a.Address), DateTimeOffset.Now);
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
            ? new Webignite.Xero.XeroDrafts(api, Documents.Put, () => Documents.List().Select(d => d.Xero.Id).OfType<string>().ToHashSet())
            : null;

    /// <summary>Approves, fetches Xero's PDF and marks documents sent, for emailing from Webignite's own mailbox.</summary>
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

    public void Dispose()
    {
        // Off the screen's thread: closing the connections continues on whatever thread it started on, so
        // waiting for it here, on that same thread, would only ever time out.
        if (mail is { } open)
            Task.Run(() => open.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));
        xero?.Dispose();
        Store.Dispose();
    }
}
