using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Webignite.Core.Web;

namespace Webignite.Services;

/// <summary>
/// The network side of the site check: DNS over HTTPS, the registry's RDAP record, the live certificate,
/// and Google's PageSpeed test. Read-only; nothing is changed anywhere. Tests hand in their own
/// <see cref="HttpClient"/> over a fake handler; the app uses <see cref="Default"/>.
/// </summary>
public sealed class SiteChecker(HttpClient http)
{
    public static SiteChecker Default { get; } = new(new HttpClient { Timeout = TimeSpan.FromSeconds(100) });

    public async Task<IReadOnlyList<DomainFinding>> DomainAsync(string domain, CancellationToken cancel = default)
    {
        var a = DnsAsync(domain, "A", 1, cancel);
        var ns = NameServersAsync(domain, cancel);
        var mx = DnsAsync(domain, "MX", 15, cancel);
        var txt = DnsAsync(domain, "TXT", 16, cancel);
        var dmarc = DnsAsync("_dmarc." + domain, "TXT", 16, cancel);
        var rdap = RdapAsync(domain, cancel);
        var cert = CertificateAsync(domain, cancel);
        var dkim = DkimAsync(domain, cancel);
        await Task.WhenAll(a, ns, mx, txt, dmarc, rdap, cert, dkim);

        return DomainReport.Build(new DomainLookup
        {
            Domain = domain,
            Today = DateOnly.FromDateTime(DateTime.Today),
            Addresses = a.Result,
            NameServers = ns.Result,
            MailServers = mx.Result,
            Txt = txt.Result,
            DmarcTxt = dmarc.Result,
            RdapJson = rdap.Result.Json,
            RdapProblem = rdap.Result.Problem,
            CertificateExpires = cert.Result.Expires,
            CertificateIssuer = cert.Result.Issuer,
            CertificateProblem = cert.Result.Problem,
            Dkim = dkim.Result,
        });
    }

    // Tries the usual DKIM selector names. A failed lookup just counts as nothing found there.
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DkimAsync(string domain, CancellationToken cancel)
    {
        var lookups = DomainReport.DkimSelectors.Select(async selector =>
        {
            try
            {
                return (selector, await DnsAsync($"{selector}._domainkey.{domain}", "TXT", 16, cancel));
            }
            catch (HttpRequestException)
            {
                return (selector, (IReadOnlyList<string>)[]);
            }
        });
        return (await Task.WhenAll(lookups)).ToDictionary(r => r.selector, r => r.Item2);
    }

    // A subdomain usually has no NS records of its own, so walk up until some are found.
    private async Task<IReadOnlyList<string>> NameServersAsync(string domain, CancellationToken cancel)
    {
        for (var name = domain; name.Contains('.'); name = name[(name.IndexOf('.') + 1)..])
        {
            var found = await DnsAsync(name, "NS", 2, cancel);
            if (found.Count > 0)
                return found;
        }
        return [];
    }

    private async Task<IReadOnlyList<string>> DnsAsync(string name, string type, int code, CancellationToken cancel)
    {
        foreach (var server in new[] { "https://cloudflare-dns.com/dns-query", "https://dns.google/resolve" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{server}?name={Uri.EscapeDataString(name)}&type={type}");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await http.SendAsync(request, cancel);
                if (response.IsSuccessStatusCode)
                    return DomainReport.DnsAnswers(await response.Content.ReadAsStringAsync(cancel), code);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // Try the next server.
            }
        }
        throw new HttpRequestException("Couldn't look up the domain. Check the internet connection.");
    }

    private async Task<(string? Json, string? Problem)> RdapAsync(string domain, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync("https://rdap.org/domain/" + Uri.EscapeDataString(domain), cancel);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, "The registry for this domain ending doesn't publish expiry dates. Check with the registrar.");
            if (!response.IsSuccessStatusCode)
                return (null, "The registry didn't answer. Try again later.");
            return (await response.Content.ReadAsStringAsync(cancel), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (null, "The registry didn't answer. Try again later.");
        }
    }

    private static async Task<(DateTimeOffset? Expires, string? Issuer, string? Problem)> CertificateAsync(string domain, CancellationToken cancel)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(domain, 443, timeout.Token);
            SslPolicyErrors errors = SslPolicyErrors.None;
            // The certificate is read, not trusted for anything: nothing is sent over this connection, and any
            // problem with it (expired, wrong name) is what the site check reports.
#pragma warning disable CA5359
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, e) =>
            {
                errors = e;
                return true; // Look at it either way; the errors are reported below.
            });
#pragma warning restore CA5359
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = domain }, timeout.Token);
            if (ssl.RemoteCertificate is null)
                return (null, null, "The site sent no certificate.");
            using var cert = new X509Certificate2(ssl.RemoteCertificate);
            var issuer = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
            var expires = new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                return (expires, issuer, $"The certificate is for a different name, so browsers warn visitors. Issued by {issuer}.");
            if (errors != SslPolicyErrors.None && expires > DateTimeOffset.UtcNow)
                return (expires, issuer, "Browsers don't trust the certificate, so visitors get a warning.");
            return (expires, issuer, null);
        }
        catch (Exception e) when (e is SocketException or IOException or AuthenticationException or OperationCanceledException)
        {
            return (null, null, "No secure (https) website answers on this domain.");
        }
    }

    /// <summary>
    /// Google's PageSpeed test, as a phone on mobile data sees the site. Takes up to a minute. Without a key the
    /// test draws on an allowance Google shares between everyone who has none, which is nearly always used up,
    /// so a key of the person's own (free from Google) is sent when there is one.
    /// </summary>
    public async Task<SpeedReport> SpeedAsync(string address, string? key = null, CancellationToken cancel = default)
    {
        var url = "https://www.googleapis.com/pagespeedonline/v5/runPagespeed?strategy=mobile&category=performance&url=" + Uri.EscapeDataString(address);
        var hasKey = !string.IsNullOrWhiteSpace(key);
        if (hasKey)
            url += "&key=" + Uri.EscapeDataString(key!.Trim());
        using var response = await http.GetAsync(url, cancel);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new SpeedQuotaException(hasKey
                ? "Your Google key has used up its tests for now. Try again in a minute; the daily allowance starts again overnight."
                : "Google only allows a few tests without a key, shared between everyone, and they are used up. Add a free key of your own below and it will work every time.");
        if (hasKey && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden
            && (await response.Content.ReadAsStringAsync(cancel)) is var why
            && (why.Contains("API key", StringComparison.OrdinalIgnoreCase) || why.Contains("API_KEY", StringComparison.Ordinal) || why.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase)
                || why.Contains("SERVICE_DISABLED", StringComparison.Ordinal)))
            throw new SpeedKeyException(why.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) || why.Contains("API_KEY_INVALID", StringComparison.Ordinal)
                ? "Google doesn't accept that key. Check it was copied whole, then save it again."
                : "Google has the key, but PageSpeed Insights isn't switched on for it. In Google Cloud, enable the PageSpeed Insights API for the key's project.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(response.StatusCode == HttpStatusCode.BadRequest || (int)response.StatusCode == 500
                ? "Google couldn't load the site. Check the address, and that the site is up."
                : $"Google's test didn't work ({(int)response.StatusCode}). Try again later.");
        return SpeedReport.Parse(await response.Content.ReadAsStringAsync(cancel));
    }
    /// <summary>
    /// One visit to a site's home page for the uptime watch: did it answer, how fast, and when the certificate
    /// runs out. A server error (500 and up) or no answer within 20 seconds counts as down.
    /// </summary>
    public static async Task<UptimePing> PingAsync(string domain, CancellationToken cancel = default)
    {
        DateTimeOffset? expires = null;
        var trusted = true;
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            SslOptions = new SslClientAuthenticationOptions
            {
                // Uptime asks whether the site answers; a bad certificate is noted (trusted = false) and shown,
                // not a reason to say the site is down. Only a plain GET is sent, nothing private.
#pragma warning disable CA5359
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    // Only the first certificate: that's the client's own domain, before any redirect elsewhere.
                    if (certificate is not null && expires is null)
                    {
                        var notAfter = certificate is X509Certificate2 full ? full.NotAfter : new X509Certificate2(certificate).NotAfter;
                        expires = new DateTimeOffset(notAfter.ToUniversalTime(), TimeSpan.Zero);
                        trusted = errors == SslPolicyErrors.None;
                    }
                    return true;
                },
#pragma warning restore CA5359
            },
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Webignite-Uptime/1.0");
        var started = System.Diagnostics.Stopwatch.StartNew();
        var at = DateTimeOffset.UtcNow;
        try
        {
            using var response = await client.GetAsync("https://" + domain + "/", HttpCompletionOption.ResponseHeadersRead, cancel);
            var ms = (int)started.ElapsedMilliseconds;
            var status = (int)response.StatusCode;
            if (status >= 500)
                return new(at, false, status, ms, expires, $"The site answered with a server error ({status}).");
            return new(at, true, status, ms, expires, trusted ? null : "Browsers don't trust the certificate.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            if (cancel.IsCancellationRequested)
                throw;
            var problem = e is TaskCanceledException ? "The site took longer than 20 seconds to answer."
                : e.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound } ? "The domain doesn't point anywhere."
                : "The site didn't answer.";
            return new(at, false, null, (int)started.ElapsedMilliseconds, expires, problem);
        }
    }
}

/// <summary>Google refused the speed test for want of allowance: with no key that is the usual case, and a key fixes it.</summary>
public sealed class SpeedQuotaException(string message) : HttpRequestException(message);

/// <summary>The person's Google key was refused, or isn't allowed to run PageSpeed tests.</summary>
public sealed class SpeedKeyException(string message) : HttpRequestException(message);
