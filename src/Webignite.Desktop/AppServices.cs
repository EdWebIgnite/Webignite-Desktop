using System.Diagnostics;
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

    /// <summary>Mailbox passwords. Windows Credential Manager on Windows; memory only elsewhere (tests and screenshots).</summary>
    public ISecretStore Secrets { get; init; } = OperatingSystem.IsWindows() ? new WindowsSecretStore() : new MemorySecretStore();

    private MailService? mail;
    public MailService Mail => mail ??= new MailService(Store, Secrets);

    public ITokenStore TokenStore { get; init; } = OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new MemoryTokenStore();

    public XeroSession? Xero =>
        xero ??= XeroSettings.ClientId is { Length: > 0 } id ? new XeroSession(new XeroOptions { ClientId = id }, Http, TokenStore) : null;

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
