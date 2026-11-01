namespace Webignite.Xero.Auth;

public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)
{
    /// <summary>Refresh a minute early so a request never starts with a token about to lapse.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt - TimeSpan.FromMinutes(1);
}

public sealed record XeroTenant(string TenantId, string TenantName, string TenantType);
