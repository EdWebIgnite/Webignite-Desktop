using Webignite.Core.Clients;

namespace Webignite.Data;

/// <summary>
/// Clients' site logins, in their own table so they never travel with the client record (to assistants, Xero or
/// anywhere else a client is read). Everything but the id is encrypted, the client it belongs to included.
/// </summary>
public sealed partial class LocalStore
{
    private const string SiteLoginContext = "sitelogin:";

    public SiteLogin SaveSiteLogin(SiteLogin login) => Put("site_logins", SiteLoginContext, login.Id, login);

    public SiteLogin? GetSiteLogin(string id) => Find<SiteLogin>("site_logins", SiteLoginContext, id);

    public void DeleteSiteLogin(string id) => Remove("site_logins", id);

    /// <summary>Every client's logins, e.g. for Ctrl+K, which finds them by site name and never by password.</summary>
    public IReadOnlyList<SiteLogin> ListSiteLogins() => ReadAll<SiteLogin>(SiteLoginContext, "SELECT id, payload FROM site_logins");

    /// <summary>One client's logins. The client id is encrypted, so every login is opened to find them.</summary>
    public IReadOnlyList<SiteLogin> ListSiteLogins(string clientId) =>
        [.. ListSiteLogins().Where(l => l.ClientId == clientId)];

    /// <summary>Removes a client and its logins as one. Returns the logins that went, for Undo.</summary>
    public IReadOnlyList<SiteLogin> DeleteClientWithLogins(string clientId) => InOneGo(() =>
    {
        var logins = ListSiteLogins(clientId);
        foreach (var login in logins)
            Remove("site_logins", login.Id);
        Remove("clients", clientId);
        return logins;
    });
}
