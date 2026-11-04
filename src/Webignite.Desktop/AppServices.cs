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
    public async Task<string?> BackUpAsync(bool now = false)
    {
        if (backingUp || Secrets.Read(BackupPasswordName) is not { Length: > 0 } password)
            return null;
        var settings = Settings.Backups;
        var started = DateTimeOffset.Now;
        if (!now && !BackupRules.IsDue(settings, started))
            return null;
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
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            var problem = e is UnauthorizedAccessException ? "Webignite can't write to the backup folder." : e.Message;
            Settings.Backups = Settings.Backups with { LastProblem = problem, LastProblemAt = DateTimeOffset.Now };
            return problem;
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
            ? new Webignite.Xero.XeroDrafts(api, Store.Save, () => Store.List().Select(d => d.XeroId).OfType<string>().ToHashSet())
            : null;

    /// <summary>Approves, fetches Xero's PDF and marks documents sent, for emailing from Webignite's own mailbox.</summary>
    public Webignite.Xero.XeroSending? XeroSending => XeroApi is { } api ? new Webignite.Xero.XeroSending(api, (stored, change) => Store.Update(stored.Id, change) ?? Store.Save(change(stored))) : null;

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
        mail?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        Store.Dispose();
    }
}
