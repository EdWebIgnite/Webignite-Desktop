namespace Webignite.Xero.Auth;

public sealed record XeroOptions
{
    /// <summary>From the "Mobile or desktop app (PKCE)" app at developer.xero.com. Not a secret.</summary>
    public required string ClientId { get; init; }

    /// <summary>Must match the redirect URI registered with Xero: http://localhost:{port}/callback</summary>
    public int RedirectPort { get; init; } = 47310;

    public string Scopes { get; init; } =
        "openid profile email offline_access accounting.invoices accounting.contacts accounting.settings.read";

    public Uri AuthorizeEndpoint { get; init; } = new("https://login.xero.com/identity/connect/authorize");
    public Uri TokenEndpoint { get; init; } = new("https://identity.xero.com/connect/token");
    public Uri ConnectionsEndpoint { get; init; } = new("https://api.xero.com/connections");

    public string RedirectUri => $"http://localhost:{RedirectPort}/callback";
}
