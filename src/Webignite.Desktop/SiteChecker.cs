using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Webignite.Core.Web;

namespace Webignite.Desktop;

/// <summary>
/// The network side of the site check: DNS over HTTPS, the registry's RDAP record, the live certificate,
/// and Google's PageSpeed test. Read-only; nothing is changed anywhere.
/// </summary>
public static class SiteChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };

    public static async Task<IReadOnlyList<DomainFinding>> DomainAsync(string domain, CancellationToken cancel = default)
    {
        var a = DnsAsync(domain, "A", 1, cancel);
        var ns = NameServersAsync(domain, cancel);
        var mx = DnsAsync(domain, "MX", 15, cancel);
        var txt = DnsAsync(domain, "TXT", 16, cancel);
        var dmarc = DnsAsync("_dmarc." + domain, "TXT", 16, cancel);
        var rdap = RdapAsync(domain, cancel);
        var cert = CertificateAsync(domain, cancel);
        await Task.WhenAll(a, ns, mx, txt, dmarc, rdap, cert);

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
        });
    }

    // A subdomain usually has no NS records of its own, so walk up until some are found.
    private static async Task<IReadOnlyList<string>> NameServersAsync(string domain, CancellationToken cancel)
    {
        for (var name = domain; name.Count(c => c == '.') >= 1; name = name[(name.IndexOf('.') + 1)..])
        {
            var found = await DnsAsync(name, "NS", 2, cancel);
            if (found.Count > 0)
                return found;
        }
        return [];
    }

    private static async Task<IReadOnlyList<string>> DnsAsync(string name, string type, int code, CancellationToken cancel)
    {
        foreach (var server in new[] { "https://cloudflare-dns.com/dns-query", "https://dns.google/resolve" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{server}?name={Uri.EscapeDataString(name)}&type={type}");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await Http.SendAsync(request, cancel);
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

    private static async Task<(string? Json, string? Problem)> RdapAsync(string domain, CancellationToken cancel)
    {
        try
        {
            using var response = await Http.GetAsync("https://rdap.org/domain/" + Uri.EscapeDataString(domain), cancel);
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
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, e) =>
            {
                errors = e;
                return true; // Look at it either way; the errors are reported below.
            });
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

    /// <summary>Google's PageSpeed test, as a phone on mobile data sees the site. Takes up to a minute.</summary>
    public static async Task<SpeedReport> SpeedAsync(string address, CancellationToken cancel = default)
    {
        var url = "https://www.googleapis.com/pagespeedonline/v5/runPagespeed?strategy=mobile&category=performance&url=" + Uri.EscapeDataString(address);
        using var response = await Http.GetAsync(url, cancel);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Google is busy with too many tests. Try again in a minute.");
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
