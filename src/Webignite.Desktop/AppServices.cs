using System.Diagnostics;
using Webignite.Core.Backups;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;
using Webignite.Data;
using Webignite.Mail;
using Webignite.Xero.Auth;

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

/// <summary>What the screens share: the local database, settings and the Xero sign-in.</summary>
public sealed class AppServices(LocalStore store) : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private XeroSession? xero;

    public LocalStore Store { get; } = store;

    public DocumentSettings DocumentSettings
    {
        get => Store.GetSetting<DocumentSettings>("documents") ?? new DocumentSettings();
        set => Store.SetSetting("documents", value);
    }

    public TaskSettings TaskSettings
    {
        get => Store.GetSetting<TaskSettings>("tasks") ?? new TaskSettings();
        set => Store.SetSetting("tasks", value);
    }

    /// <summary>The invoice and quote email wording, pasted from Xero.</summary>
    public EmailTemplates EmailTemplates
    {
        get => Store.GetSetting<EmailTemplates>("email-templates") ?? new EmailTemplates();
        set => Store.SetSetting("email-templates", value);
    }

    public XeroSettings XeroSettings
    {
        get => Store.GetSetting<XeroSettings>("xero") ?? new XeroSettings();
        set
        {
            if (value.ClientId != XeroSettings.ClientId)
                xero = null;
            Store.SetSetting("xero", value);
        }
    }

    // Backups

    public const string BackupPasswordName = "Backup/password";

    public BackupSettings BackupSettings
    {
        get => Store.GetSetting<BackupSettings>("backups") ?? new BackupSettings();
        set => Store.SetSetting("backups", value);
    }

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

    public string BackupFolder => BackupSettings.Folder is { Length: > 0 } folder ? folder : DefaultBackupFolder();

    private bool backingUp;

    /// <summary>
    /// Makes a backup when one is due (or now, when asked). Copies the database on this thread, then encrypts
    /// in the background. Returns the problem, or null when it worked or wasn't needed.
    /// </summary>
    public async Task<string?> BackUpAsync(bool now = false)
    {
        if (backingUp || Secrets.Read(BackupPasswordName) is not { Length: > 0 } password)
            return null;
        var settings = BackupSettings;
        var started = DateTimeOffset.Now;
        if (!now && !BackupRules.IsDue(settings, started))
            return null;
        backingUp = true;
        try
        {
            var snapshot = Backups.Snapshot(Store);
            var key = VaultKeySource();
            var folder = BackupFolder;
            var version = typeof(AppServices).Assembly.GetName().Version?.ToString(3);
            var file = await Task.Run(() => Backups.Seal(snapshot, key, password, folder, started, version));
            BackupSettings = BackupSettings with { LastBackup = started, LastFile = file, LastProblem = null, LastProblemAt = null };
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            var problem = e is UnauthorizedAccessException ? "Webignite can't write to the backup folder." : e.Message;
            BackupSettings = BackupSettings with { LastProblem = problem, LastProblemAt = DateTimeOffset.Now };
            return problem;
        }
        finally
        {
            backingUp = false;
        }
    }

    /// <summary>Mailbox passwords. Windows Credential Manager on Windows; memory only elsewhere (tests and screenshots).</summary>
    public ISecretStore Secrets { get; init; } = OperatingSystem.IsWindows() ? new WindowsSecretStore() : new MemorySecretStore();

    /// <summary>Set by the main window, so other pages can open a mail or start one in the Mail page.</summary>
    public Action<Webignite.Core.Mail.MailSummary>? OpenMail { get; set; }

    public Action<IReadOnlyList<Webignite.Core.Mail.MailPerson>>? WriteMail { get; set; }

    private MailService? mail;
    public MailService Mail => mail ??= new MailService(Store, Secrets);

    public ITokenStore TokenStore { get; init; } = OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new MemoryTokenStore();

    public XeroSession? Xero =>
        xero ??= XeroSettings.ClientId is { Length: > 0 } id ? new XeroSession(new XeroOptions { ClientId = id }, Http, TokenStore) : null;

    /// <summary>Xero's accounting API for the connected organisation, or null when Xero isn't connected.</summary>
    public Webignite.Xero.XeroApi? XeroApi =>
        Xero is { HasSavedSignIn: true } session && XeroSettings.TenantId is { Length: > 0 } tenant ? new Webignite.Xero.XeroApi(session, Http, tenant) : null;

    /// <summary>Creates Xero drafts, saving each step to this PC as it goes.</summary>
    public Webignite.Xero.XeroDrafts? XeroDrafts =>
        XeroApi is { } api
            ? new Webignite.Xero.XeroDrafts(api, Store.Save, () => Store.List().Select(d => d.XeroId).OfType<string>().ToHashSet())
            : null;

    /// <summary>Approves, fetches Xero's PDF and marks documents sent, for emailing from Webignite's own mailbox.</summary>
    public Webignite.Xero.XeroSending? XeroSending => XeroApi is { } api ? new Webignite.Xero.XeroSending(api, Store.Save) : null;

    /// <summary>
    /// Set by the main window: opens a prepared mail in the Mail page, with a note shown above it. The action
    /// runs after it was sent. Returns false when another mail with changes is still open.
    /// </summary>
    public Func<Webignite.Core.Mail.OutgoingMail, string?, Func<Task>?, bool>? ComposeMail { get; set; }

    /// <summary>
    /// Does what the calendar calls for since the app last looked: drafts retainer and renewal invoices that are due,
    /// adds monthly expenses and renewal reminders. All of it stays on this PC and safe to run any number of times.
    /// </summary>
    public void CatchUp()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Store.DraftRetainerInvoices(DocumentSettings, today);
        Store.AddRecurringExpenses(today);
        Store.RaiseRenewals(DocumentSettings, today);
    }

    /// <summary>Opens a link in the person's own default browser. Nothing is embedded in the app.</summary>
    public static Task OpenInBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        mail?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        Store.Dispose();
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
}
