using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Webignite.Core.Web;

namespace Webignite.Services;

/// <summary>A public resolver that answers over HTTPS (RFC 8484), as the DNS tool asks them.</summary>
public sealed record DnsResolver(string Name, Uri Address);

/// <summary>What the "has it spread?" check found: each place asked, and how long the record may be remembered.</summary>
public sealed record DnsSpreadResult(IReadOnlyList<DnsSighting> Sightings, int? TtlSeconds);

/// <summary>
/// The network side of the DNS tool. Public resolvers are asked over HTTPS, so a lookup works wherever the web
/// does; a domain's own name servers only speak plain DNS (port 53), so they are asked directly, and a network that
/// blocks that just means they are left out. Read-only. Tests hand in their own two ways of sending.
/// </summary>
public sealed class DnsClient(
    Func<Uri, byte[], CancellationToken, Task<byte[]>> overHttps,
    Func<IPAddress, byte[], CancellationToken, Task<byte[]>> direct)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static DnsClient Default { get; } = new(PostAsync, AskDirectAsync);

    /// <summary>The resolvers most of the world's lookups go through. The first two also answer ordinary lookups.</summary>
    public static readonly IReadOnlyList<DnsResolver> Resolvers =
    [
        new("Cloudflare (1.1.1.1)", new Uri("https://cloudflare-dns.com/dns-query")),
        new("Google (8.8.8.8)", new Uri("https://dns.google/dns-query")),
        new("Quad9 (9.9.9.9)", new Uri("https://dns.quad9.net/dns-query")),
        new("OpenDNS", new Uri("https://doh.opendns.com/dns-query")),
        new("AdGuard", new Uri("https://dns.adguard-dns.com/dns-query")),
    ];

    /// <summary>The most of a domain's own name servers to ask; they should all say the same.</summary>
    public const int MostOwnServers = 4;

    /// <summary>One lookup, through the first resolver that answers.</summary>
    public async Task<DnsAnswer> LookupAsync(string name, DnsType type, CancellationToken cancel = default)
    {
        foreach (var resolver in Resolvers.Take(2))
        {
            try
            {
                return DnsMessage.Read(await overHttps(resolver.Address, DnsMessage.Question(name, type), cancel));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
            {
                // Try the next resolver.
            }
        }
        throw new HttpRequestException("Couldn't look that up. Check the internet connection.");
    }

    /// <summary>Asks the domain's own name servers and each public resolver for the same record, all at once.</summary>
    public async Task<DnsSpreadResult> SpreadAsync(string name, DnsType type, CancellationToken cancel = default)
    {
        var own = OwnServersAsync(name, type, cancel);
        var others = Resolvers.Select(async resolver =>
        {
            try
            {
                var answer = DnsMessage.Read(await overHttps(resolver.Address, DnsMessage.Question(name, type), cancel));
                return new DnsSighting(resolver.Name, false, DnsSpread.ValuesOf(answer, type), answer.Refused ? "It wouldn't answer." : null);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return new DnsSighting(resolver.Name, false, [], "No answer.");
            }
        }).ToList();
        await Task.WhenAll(others.Cast<Task>().Append(own));
        var (sightings, ttl) = own.Result;
        return new DnsSpreadResult([.. sightings, .. others.Select(o => o.Result)], ttl);
    }

    private async Task<(IReadOnlyList<DnsSighting> Sightings, int? Ttl)> OwnServersAsync(string name, DnsType type, CancellationToken cancel)
    {
        try
        {
            // A name inside a domain has no name servers of its own, so walk up until some are found.
            IReadOnlyList<string> servers = [];
            for (var zone = name; zone.Contains('.') && servers.Count == 0; zone = zone[(zone.IndexOf('.') + 1)..])
                servers = (await LookupAsync(zone, DnsType.NS, cancel)).Values(DnsType.NS);
            int? ttl = null;
            var asked = servers.Take(MostOwnServers).Select(async server =>
            {
                try
                {
                    var addresses = (await LookupAsync(server, DnsType.A, cancel)).Values(DnsType.A);
                    if (addresses.Count == 0 || !IPAddress.TryParse(addresses[0], out var address))
                        return new DnsSighting(server, true, [], "Its address couldn't be found.");
                    var answer = DnsMessage.Read(await direct(address, DnsMessage.Question(name, type, (ushort)Random.Shared.Next(1, ushort.MaxValue), recurse: false), cancel));
                    if (answer.Records.Count > 0)
                        ttl = answer.Records.Min(r => r.Ttl);
                    return new DnsSighting(server, true, DnsSpread.ValuesOf(answer, type), answer.Refused ? "It wouldn't answer." : null);
                }
                catch (Exception e) when (e is SocketException or IOException or HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    return new DnsSighting(server, true, [], "Couldn't be reached from this network.");
                }
            }).ToList();
            await Task.WhenAll(asked);
            return ([.. asked.Select(a => a.Result)], ttl);
        }
        catch (HttpRequestException)
        {
            return ([], null);
        }
    }

    private static async Task<byte[]> PostAsync(Uri resolver, byte[] question, CancellationToken cancel)
    {
        using var content = new ByteArrayContent(question);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        using var request = new HttpRequestMessage(HttpMethod.Post, resolver) { Content = content };
        request.Headers.Accept.ParseAdd("application/dns-message");
        using var response = await Http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancel);
    }

    // Plain DNS on port 53: a UDP packet, and the same question over TCP when the answer was too big for one.
    private static async Task<byte[]> AskDirectAsync(IPAddress server, byte[] question, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using (var udp = new UdpClient(server.AddressFamily))
        {
            udp.Connect(server, 53);
            await udp.SendAsync(question, timeout.Token);
            var reply = (await udp.ReceiveAsync(timeout.Token)).Buffer;
            if (!DnsMessage.Read(reply).Truncated)
                return reply;
        }
        using var tcp = new TcpClient(server.AddressFamily);
        await tcp.ConnectAsync(server, 53, timeout.Token);
        var stream = tcp.GetStream();
        await stream.WriteAsync(new[] { (byte)(question.Length >> 8), (byte)question.Length }, timeout.Token);
        await stream.WriteAsync(question, timeout.Token);
        var size = new byte[2];
        await stream.ReadExactlyAsync(size, timeout.Token);
        var whole = new byte[(size[0] << 8) | size[1]];
        await stream.ReadExactlyAsync(whole, timeout.Token);
        return whole;
    }
}
