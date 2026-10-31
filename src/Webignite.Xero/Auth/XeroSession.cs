using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webignite.Xero.Auth;

/// <summary>
/// Signs in to Xero from the desktop and keeps a valid access token. Xero issues a new refresh token
/// on every refresh, so the new one is saved straight away; losing it would mean signing in again.
/// </summary>
public sealed class XeroSession(XeroOptions options, HttpClient http, ITokenStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TokenSet? tokens;

    public bool HasSavedSignIn => store.ReadRefreshToken() is not null;

    public Uri AuthorizeUrl(string state, string challenge)
    {
        var query = string.Join("&", new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["scope"] = options.Scopes,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        }.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));
        return new Uri(options.AuthorizeEndpoint + "?" + query);
    }

    /// <summary>
    /// Opens Xero's sign-in page in the person's own browser and waits for them to finish.
    /// Returns the organisations they gave access to.
    /// </summary>
    public async Task<IReadOnlyList<XeroTenant>> SignInAsync(Func<Uri, Task> openBrowser, CancellationToken cancel)
    {
        var verifier = Pkce.NewVerifier();
        var state = Pkce.NewState();
        using var receiver = new LoopbackReceiver(options.RedirectPort);
        await openBrowser(AuthorizeUrl(state, Pkce.Challenge(verifier)));
        var code = await receiver.WaitForCodeAsync(state, cancel);
        await ExchangeAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = options.ClientId,
            ["code"] = code,
            ["redirect_uri"] = options.RedirectUri,
            ["code_verifier"] = verifier,
        }, cancel);
        return await TenantsAsync(cancel);
    }

    public async Task<string> AccessTokenAsync(CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            if (tokens is not null && !tokens.IsExpired(clock.GetUtcNow()))
                return tokens.AccessToken;
            var refresh = store.ReadRefreshToken() ?? throw new XeroSignInException("Not signed in to Xero.");
            await ExchangeAsync(new()
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = options.ClientId,
                ["refresh_token"] = refresh,
            }, cancel);
            return tokens!.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<XeroTenant>> TenantsAsync(CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.ConnectionsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(cancel));
        using var response = await http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        var connections = await response.Content.ReadFromJsonAsync<List<Connection>>(cancel) ?? [];
        return connections
            .Where(c => c.TenantType == "ORGANISATION")
            .Select(c => new XeroTenant(c.TenantId, c.TenantName ?? "", c.TenantType))
            .ToList();
    }

    public void SignOut()
    {
        store.Clear();
        tokens = null;
    }

    private async Task ExchangeAsync(Dictionary<string, string> form, CancellationToken cancel)
    {
        using var response = await http.PostAsync(options.TokenEndpoint, new FormUrlEncodedContent(form), cancel);
        if (!response.IsSuccessStatusCode)
        {
            if (form["grant_type"] == "refresh_token" && (int)response.StatusCode is 400 or 401)
            {
                // The saved sign-in expired (60 days unused) or was revoked in Xero.
                SignOut();
                throw new XeroSignInException("The Xero sign-in has expired. Please connect Xero again.");
            }
            throw new XeroSignInException($"Xero sign-in failed ({(int)response.StatusCode}).");
        }
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancel)
                    ?? throw new XeroSignInException("Xero sent an empty sign-in reply.");
        store.WriteRefreshToken(token.RefreshToken);
        tokens = new TokenSet(token.AccessToken, token.RefreshToken, clock.GetUtcNow().AddSeconds(token.ExpiresIn));
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record Connection(
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("tenantName")] string? TenantName,
        [property: JsonPropertyName("tenantType")] string TenantType);
}
