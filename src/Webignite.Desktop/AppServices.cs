using System.Diagnostics;
using Webignite.Core.Documents;
using Webignite.Data;
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

    public ITokenStore TokenStore { get; init; } = OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new MemoryTokenStore();

    public XeroSession? Xero =>
        xero ??= XeroSettings.ClientId is { Length: > 0 } id ? new XeroSession(new XeroOptions { ClientId = id }, Http, TokenStore) : null;

    /// <summary>Opens a link in the person's own default browser. Nothing is embedded in the app.</summary>
    public static Task OpenInBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public void Dispose() => Store.Dispose();
}
