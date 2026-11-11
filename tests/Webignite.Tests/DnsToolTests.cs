using System.Net;
using System.Text;
using Webignite.Core.Web;
using Webignite.Services;

namespace Webignite.Tests;

public sealed class DnsToolTests
{
    // An answer as a server would send it: the question back, then each record pointing at the question's name.
    internal static byte[] Reply(string name, DnsType asked, params (DnsType Type, int Ttl, byte[] Data)[] records) =>
        Reply(name, asked, 0, false, records);

    internal static byte[] Reply(string name, DnsType asked, int code, bool truncated, params (DnsType Type, int Ttl, byte[] Data)[] records)
    {
        var bytes = new List<byte>(DnsMessage.Question(name, asked));
        bytes[2] = (byte)(0x80 | (truncated ? 0x02 : 0) | 0x01);
        bytes[3] = (byte)(0x80 | code);
        bytes[7] = (byte)records.Length;
        foreach (var (type, ttl, data) in records)
        {
            bytes.AddRange([0xC0, 0x0C, (byte)((int)type >> 8), (byte)type, 0, 1]);
            bytes.AddRange([(byte)(ttl >> 24), (byte)(ttl >> 16), (byte)(ttl >> 8), (byte)ttl, (byte)(data.Length >> 8), (byte)data.Length]);
            bytes.AddRange(data);
        }
        return [.. bytes];
    }

    internal static byte[] Labels(string name) => [.. DnsMessage.Question(name, DnsType.A).Skip(12).SkipLast(4)];

    internal static byte[] Text(params string[] strings) => [.. strings.SelectMany(s => new[] { (byte)s.Length }.Concat(Encoding.ASCII.GetBytes(s)))];

    [Fact]
    public void A_question_is_the_name_and_kind_as_they_travel()
    {
        var question = DnsMessage.Question("Karoo.co.za.", DnsType.MX, id: 0x1234);
        Assert.Equal([0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0], question[..12]);
        Assert.Equal([5, .. "Karoo"u8, 2, .. "co"u8, 2, .. "za"u8, 0, 0, 15, 0, 1], question[12..]);
        Assert.Equal(0, DnsMessage.Question("x.co.za", DnsType.A, recurse: false)[2]);
        Assert.Equal([0x01, 0x01], DnsMessage.Question("x.co.za", DnsType.CAA)[^4..^2]);
        Assert.Throws<ArgumentException>(() => DnsMessage.Question(new string('a', 64) + ".co.za", DnsType.A));
    }

    [Fact]
    public void Each_kind_of_record_reads_as_people_write_it()
    {
        const string name = "karoo.co.za";
        var answer = DnsMessage.Read(Reply(name, DnsType.A,
            (DnsType.A, 300, [41, 203, 18, 10]),
            (DnsType.AAAA, 300, IPAddress.Parse("2001:db8::1").GetAddressBytes()),
            (DnsType.MX, 3600, [0, 10, .. Labels("mail.karoo.co.za")]),
            (DnsType.NS, 86400, Labels("ns1.host-h.net")),
            (DnsType.CNAME, 60, [3, .. "www"u8, 0xC0, 0x0C]),
            (DnsType.TXT, 300, Text("v=spf1 include:spf.host-h.net ", "~all")),
            (DnsType.SOA, 300, [.. Labels("ns1.host-h.net"), .. Labels("postmaster.host-h.net"), 0x78, 0x9A, 0xBC, 0xDE, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1]),
            (DnsType.SRV, 300, [0, 10, 0, 5, 0x01, 0xBB, .. Labels("sip.karoo.co.za")]),
            (DnsType.CAA, 300, [0, 5, .. "issue"u8, .. "letsencrypt.org"u8])));

        Assert.Equal(0, answer.Code);
        Assert.False(answer.NoSuchName);
        Assert.Equal(["41.203.18.10"], answer.Values(DnsType.A));
        Assert.Equal(["2001:db8::1"], answer.Values(DnsType.AAAA));
        Assert.Equal(["10 mail.karoo.co.za"], answer.Values(DnsType.MX));
        Assert.Equal(["ns1.host-h.net"], answer.Values(DnsType.NS));
        Assert.Equal(["www.karoo.co.za"], answer.Values(DnsType.CNAME));
        Assert.Equal(["v=spf1 include:spf.host-h.net ~all"], answer.Values(DnsType.TXT));
        Assert.Equal(["ns1.host-h.net postmaster.host-h.net serial 2023406814"], answer.Values(DnsType.SOA));
        Assert.Equal(["10 5 443 sip.karoo.co.za"], answer.Values(DnsType.SRV));
        Assert.Equal(["0 issue \"letsencrypt.org\""], answer.Values(DnsType.CAA));
        Assert.All(answer.Records, r => Assert.Equal(name, r.Name));
        Assert.Equal(86400, answer.Records.First(r => r.Type == DnsType.NS).Ttl);
    }

    [Fact]
    public void A_missing_name_a_cut_off_answer_and_nonsense_are_told_apart()
    {
        Assert.True(DnsMessage.Read(Reply("nothere.co.za", DnsType.A, 3, false)).NoSuchName);
        Assert.True(DnsMessage.Read(Reply("x.co.za", DnsType.A, 5, false)).Refused);
        Assert.True(DnsMessage.Read(Reply("x.co.za", DnsType.TXT, 0, true)).Truncated);
        Assert.Empty(DnsMessage.Read(Reply("x.co.za", DnsType.A)).Records);

        // Too short, a record that runs off the end, a name that points at itself: a failed answer, never a crash.
        Assert.Equal(2, DnsMessage.Read([1, 2, 3]).Code);
        var cut = Reply("x.co.za", DnsType.A, (DnsType.A, 60, [1, 2, 3, 4]));
        Assert.Equal(2, DnsMessage.Read(cut.AsSpan(0, cut.Length - 3)).Code);
        byte[] loop = [0, 0, 0x81, 0x80, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 0x0C, 0, 1, 0, 1];
        Assert.Equal(2, DnsMessage.Read(loop).Code);
        // A kind this tool doesn't show is skipped, and what follows it is still read.
        var mixed = Reply("x.co.za", DnsType.A, ((DnsType)99, 60, [9, 9]), (DnsType.A, 60, [1, 2, 3, 4]));
        Assert.Equal(["1.2.3.4"], DnsMessage.Read(mixed).Values(DnsType.A));
    }

    [Theory]
    [InlineData("karoo.co.za", DnsType.A, "karoo.co.za")]
    [InlineData(" https://www.Karoo.co.za/menu?x=1 ", DnsType.A, "www.karoo.co.za")]
    [InlineData("_dmarc.karoo.co.za", DnsType.TXT, "_dmarc.karoo.co.za")]
    [InlineData("default._domainkey.karoo.co.za.", DnsType.TXT, "default._domainkey.karoo.co.za")]
    [InlineData("eddie@webignite.co.za", DnsType.MX, "webignite.co.za")]
    [InlineData("41.203.18.10", DnsType.PTR, "10.18.203.41.in-addr.arpa")]
    [InlineData("café.co.za", DnsType.A, "xn--caf-dma.co.za")]
    public void What_is_typed_becomes_the_name_to_ask_for(string typed, DnsType type, string expected) =>
        Assert.Equal(expected, DnsMessage.NameFor(typed, type));

    [Fact]
    public void Nonsense_is_not_a_name()
    {
        Assert.Null(DnsMessage.NameFor("", DnsType.A));
        Assert.Null(DnsMessage.NameFor("karoo", DnsType.A));
        Assert.Null(DnsMessage.NameFor("two words.co.za", DnsType.A));
        Assert.Null(DnsMessage.NameFor("a..co.za", DnsType.A));
        Assert.EndsWith(".ip6.arpa", DnsMessage.NameFor("2001:db8::1", DnsType.PTR));
        Assert.Equal(72, DnsMessage.NameFor("2001:db8::1", DnsType.PTR)!.Length);
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(59, "59 seconds")]
    [InlineData(300, "5 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(86400, "24 hours")]
    [InlineData(172800, "2 days")]
    public void A_lifetime_reads_in_words(int seconds, string expected) => Assert.Equal(expected, DnsMessage.Lifetime(seconds));

    [Fact]
    public void Spread_is_judged_against_the_domains_own_servers()
    {
        var own = new DnsSighting("ns1.host-h.net", true, ["41.203.18.99"]);
        var caughtUp = new DnsSighting("Cloudflare", false, ["41.203.18.99"]);
        var behind = new DnsSighting("Google", false, ["41.203.18.10"]);
        var silent = new DnsSighting("Quad9", false, [], "No answer.");

        Assert.StartsWith("It has spread", DnsSpread.Verdict([own, caughtUp, silent]));
        Assert.Equal(
            "Still spreading: 1 of 2 resolvers give an older answer than the domain's own name servers. It can take up to 1 hour more, the time the old record may be remembered.",
            DnsSpread.Verdict([own, caughtUp, behind, silent], 3600));
        Assert.True(DnsSpread.Matches(caughtUp, [own, caughtUp, behind]));
        Assert.False(DnsSpread.Matches(behind, [own, caughtUp, behind]));
        Assert.Null(DnsSpread.Matches(silent, [own, silent]));
        Assert.Null(DnsSpread.Matches(caughtUp, [caughtUp, behind]));
    }

    [Fact]
    public void Spread_says_so_when_there_is_less_to_go_on()
    {
        var a = new DnsSighting("Cloudflare", false, ["1.2.3.4"]);
        var b = new DnsSighting("Google", false, ["1.2.3.5"]);
        var unreachable = new DnsSighting("ns1.x.net", true, [], "Couldn't be reached from this network.");
        Assert.StartsWith("Every resolver asked gives the same answer.", DnsSpread.Verdict([unreachable, a, a with { Where = "Google" }]));
        Assert.StartsWith("The resolvers don't all agree", DnsSpread.Verdict([unreachable, a, b]));
        Assert.StartsWith("The domain's own name servers don't agree", DnsSpread.Verdict([new("ns1", true, ["1.2.3.4"]), new("ns2", true, ["1.2.3.5"]), a]));
        Assert.StartsWith("Nobody answered", DnsSpread.Verdict([unreachable]));
        Assert.StartsWith("Only the domain's own", DnsSpread.Verdict([new("ns1", true, ["1.2.3.4"])]));
        // Order and letter case don't count as a difference.
        Assert.StartsWith("It has spread", DnsSpread.Verdict([new("ns1", true, ["10 Mail.x.co.za", "20 b.x.co.za"]), new("Google", false, ["20 b.x.co.za", "10 mail.x.co.za."])]));
    }

    [Fact]
    public void An_alias_is_compared_by_what_it_points_at()
    {
        var ownSays = DnsMessage.Read(Reply("www.karoo.co.za", DnsType.A, (DnsType.CNAME, 300, Labels("karoo.co.za"))));
        var resolverSays = DnsMessage.Read(Reply("www.karoo.co.za", DnsType.A, (DnsType.CNAME, 120, Labels("karoo.co.za")), (DnsType.A, 60, [41, 203, 18, 10])));
        Assert.Equal(["alias of karoo.co.za"], DnsSpread.ValuesOf(ownSays, DnsType.A));
        Assert.Equal(DnsSpread.ValuesOf(ownSays, DnsType.A), DnsSpread.ValuesOf(resolverSays, DnsType.A));
        Assert.Equal(["karoo.co.za"], DnsSpread.ValuesOf(ownSays, DnsType.CNAME));
    }

    [Fact]
    public void An_spf_record_lists_who_may_send()
    {
        var xneelo = MailRecords.KnownSenders.First(s => s.Name == "xneelo");
        var google = MailRecords.KnownSenders.First(s => s.Name == "Google Workspace");
        var built = MailRecords.Spf(new SpfChoices { Senders = [xneelo, google], WebServer = true, Others = "41.203.18.10, mailer.example.com 2001:db8::/32 include:spf.host-h.net ip4:10.0.0.0/8 ???" });
        Assert.Equal("@", built.Host);
        Assert.Equal("TXT", built.Type);
        Assert.Equal("v=spf1 mx a include:spf.host-h.net include:_spf.google.com ip4:41.203.18.10 include:mailer.example.com ip6:2001:db8::/32 ip4:10.0.0.0/8 ~all", built.Value);
        Assert.Contains(built.Notes, n => n.StartsWith("\"???\" isn't a server's address", StringComparison.Ordinal));
        Assert.Contains(built.Notes, n => n.StartsWith("A domain may have only one SPF record", StringComparison.Ordinal));
        Assert.Equal(5, MailRecords.Lookups(built.Value));
    }

    [Fact]
    public void An_spf_record_warns_before_it_would_fail()
    {
        var all = MailRecords.Spf(new SpfChoices { Senders = MailRecords.KnownSenders, WebServer = true, Others = "a.example.com b.example.com", Ending = SpfEnding.Hard });
        Assert.EndsWith(" -all", all.Value);
        Assert.Equal(12, MailRecords.Lookups(all.Value));
        Assert.Contains(all.Notes, n => n.StartsWith("This needs 12 lookups and mail servers stop at 10", StringComparison.Ordinal));
        Assert.Contains(all.Notes, n => n.StartsWith("-all refuses mail", StringComparison.Ordinal));

        var nothing = MailRecords.Spf(new SpfChoices { MailServers = false });
        Assert.Equal("v=spf1 ~all", nothing.Value);
        Assert.Contains(nothing.Notes, n => n.StartsWith("Nothing may send mail", StringComparison.Ordinal));
        Assert.Contains(MailRecords.Spf(new SpfChoices { Others = string.Join(' ', Enumerable.Range(1, 30).Select(i => $"ip4:192.168.100.{i}")) }).Notes,
            n => n.StartsWith("It is longer than 255 characters", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dmarc_record_starts_by_watching()
    {
        var watching = MailRecords.Dmarc(DmarcPolicy.None, " dmarc@webignite.co.za ", 50);
        Assert.Equal("_dmarc", watching.Host);
        Assert.Equal("v=DMARC1; p=none; rua=mailto:dmarc@webignite.co.za", watching.Value);
        Assert.Contains(watching.Notes, n => n.StartsWith("p=none only watches", StringComparison.Ordinal));

        Assert.Equal("v=DMARC1; p=quarantine; rua=mailto:a@x.co.za; pct=25", MailRecords.Dmarc(DmarcPolicy.Quarantine, "a@x.co.za", 25).Value);
        Assert.Equal("v=DMARC1; p=reject; rua=mailto:a@x.co.za", MailRecords.Dmarc(DmarcPolicy.Reject, "a@x.co.za", 500).Value);

        var blind = MailRecords.Dmarc(DmarcPolicy.Reject, "");
        Assert.Equal("v=DMARC1; p=reject", blind.Value);
        Assert.Contains(blind.Notes, n => n.StartsWith("With no address for reports", StringComparison.Ordinal));
        Assert.Contains(MailRecords.Dmarc(DmarcPolicy.None, "not an address").Notes, n => n.StartsWith("That doesn't look like an email address", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dkim_key_goes_under_its_selector()
    {
        Assert.Equal("default._domainkey", MailRecords.DkimHost(" Default "));
        Assert.Equal("selector1._domainkey", MailRecords.DkimHost("selector1._domainkey"));
        Assert.Equal("", MailRecords.DkimHost(" "));
        Assert.StartsWith("Nothing is there", MailRecords.DkimVerdict([]));
        Assert.StartsWith("There is a record, but", MailRecords.DkimVerdict(["v=spf1 ~all"]));
        Assert.StartsWith("The key has been withdrawn", MailRecords.DkimVerdict(["v=DKIM1; k=rsa; p="]));
        Assert.StartsWith("A DKIM key is there. It is a short one", MailRecords.DkimVerdict(["v=DKIM1; p=" + new string('A', 160)]));
        Assert.Equal("A DKIM key is there and looks right.", MailRecords.DkimVerdict(["v=DKIM1; k=rsa; p=" + new string('A', 392)]));
    }

    // A pretend internet: each resolver and name server answers from a table.
    private static DnsClient Client(Dictionary<string, Func<string, DnsType, byte[]?>> resolvers, Dictionary<string, Func<string, DnsType, byte[]?>> servers) =>
        new(
            (uri, question, _) => Answer(resolvers.GetValueOrDefault(uri.Host), question, () => new HttpRequestException("down")),
            (address, question, _) => Answer(servers.GetValueOrDefault(address.ToString()), question, () => new System.Net.Sockets.SocketException()));

    private static Task<byte[]> Answer(Func<string, DnsType, byte[]?>? table, byte[] question, Func<Exception> down)
    {
        var at = 12;
        var labels = new List<string>();
        while (question[at] != 0)
        {
            labels.Add(Encoding.ASCII.GetString(question, at + 1, question[at]));
            at += 1 + question[at];
        }
        var type = (DnsType)((question[at + 1] << 8) | question[at + 2]);
        var name = string.Join('.', labels);
        return table?.Invoke(name, type) is { } reply ? Task.FromResult(reply) : Task.FromException<byte[]>(down());
    }

    [Fact]
    public async Task A_lookup_falls_back_to_the_next_resolver()
    {
        var client = Client(new() { ["dns.google"] = (name, type) => Reply(name, type, (DnsType.A, 60, [1, 2, 3, 4])) }, []);
        Assert.Equal(["1.2.3.4"], (await client.LookupAsync("karoo.co.za", DnsType.A, TestContext.Current.CancellationToken)).Values(DnsType.A));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client([], []).LookupAsync("karoo.co.za", DnsType.A, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_spread_check_asks_the_domains_own_servers_and_every_resolver()
    {
        byte[]? Resolver(string name, DnsType type, byte last) => (name, type) switch
        {
            ("www.karoo.co.za", DnsType.NS) => Reply(name, type),
            ("karoo.co.za", DnsType.NS) => Reply(name, type, (DnsType.NS, 3600, Labels("ns1.host-h.net")), (DnsType.NS, 3600, Labels("ns2.dns-h.com"))),
            ("ns1.host-h.net", DnsType.A) => Reply(name, type, (DnsType.A, 3600, [10, 0, 0, 1])),
            ("ns2.dns-h.com", DnsType.A) => Reply(name, type, (DnsType.A, 3600, [10, 0, 0, 2])),
            ("www.karoo.co.za", DnsType.A) => Reply(name, type, (DnsType.A, 200, [41, 203, 18, last])),
            _ => null,
        };
        var client = Client(
            new()
            {
                ["cloudflare-dns.com"] = (n, t) => Resolver(n, t, 99),
                ["dns.google"] = (n, t) => Resolver(n, t, 10),
                ["dns.quad9.net"] = (n, t) => Resolver(n, t, 99),
                ["doh.opendns.com"] = (n, t) => t == DnsType.A ? Reply(n, t, 5, false) : null,
            },
            new() { ["10.0.0.1"] = (n, t) => Reply(n, t, (DnsType.A, 900, [41, 203, 18, 99])) });

        var result = await client.SpreadAsync("www.karoo.co.za", DnsType.A, TestContext.Current.CancellationToken);

        Assert.Equal(900, result.TtlSeconds);
        Assert.Equal(["ns1.host-h.net", "ns2.dns-h.com", .. DnsClient.Resolvers.Select(r => r.Name)], result.Sightings.Select(s => s.Where));
        Assert.Equal(["41.203.18.99"], result.Sightings[0].Values);
        Assert.True(result.Sightings[0].IsOwnServer);
        Assert.Equal("Couldn't be reached from this network.", result.Sightings[1].Problem);
        Assert.Equal(["41.203.18.10"], result.Sightings.First(s => s.Where.StartsWith("Google", StringComparison.Ordinal)).Values);
        Assert.Equal("It wouldn't answer.", result.Sightings.First(s => s.Where == "OpenDNS").Problem);
        Assert.Equal("No answer.", result.Sightings.First(s => s.Where == "AdGuard").Problem);
        Assert.Equal(
            "Still spreading: 1 of 3 resolvers give an older answer than the domain's own name servers. It can take up to 15 minutes more, the time the old record may be remembered.",
            DnsSpread.Verdict(result.Sightings, result.TtlSeconds));
    }
}
