using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;

namespace Webignite.Core.Web;

/// <summary>The kinds of DNS record the DNS tool looks up, with the number each has on the wire.</summary>
public enum DnsType
{
    A = 1,
    NS = 2,
    CNAME = 5,
    SOA = 6,
    PTR = 12,
    MX = 15,
    TXT = 16,
    AAAA = 28,
    SRV = 33,
    CAA = 257,
}

/// <summary>One record in an answer: its name, kind, how long it may be remembered, and what it says.</summary>
public sealed record DnsRecord(string Name, DnsType Type, int Ttl, string Value);

/// <summary>What a server said: the records, or why there are none.</summary>
public sealed record DnsAnswer(IReadOnlyList<DnsRecord> Records, int Code, bool Truncated)
{
    /// <summary>The name doesn't exist at all (NXDOMAIN), as against existing with no record of this kind.</summary>
    public bool NoSuchName => Code == 3;

    public bool Refused => Code is 2 or 5;

    /// <summary>The values of the kind asked for, in an order that doesn't depend on the server's.</summary>
    public IReadOnlyList<string> Values(DnsType type) =>
        [.. Records.Where(r => r.Type == type).Select(r => r.Value).Order(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// DNS questions and answers as they travel (RFC 1035): the same bytes whether sent to a public resolver over
/// HTTPS or straight to a domain's own name server. Pure: the sending is in Services (DnsClient).
/// </summary>
public static class DnsMessage
{
    /// <summary>A question for one name and kind. <paramref name="recurse"/> asks the server to go and find out;
    /// a domain's own name server is asked without it.</summary>
    public static byte[] Question(string name, DnsType type, ushort id = 0, bool recurse = true)
    {
        var bytes = new List<byte>(32 + name.Length);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        header[2] = (byte)(recurse ? 1 : 0);
        header[5] = 1;
        bytes.AddRange(header);
        foreach (var label in Ascii(name).TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (label.Length > 63)
                throw new ArgumentException("A part of that name is too long.", nameof(name));
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.Add(0);
        bytes.Add((byte)((int)type >> 8));
        bytes.Add((byte)type);
        bytes.Add(0);
        bytes.Add(1);
        return [.. bytes];
    }

    /// <summary>Reads an answer. Records of kinds this tool doesn't show are left out; a cut-off or nonsense
    /// message reads as a server failure rather than throwing.</summary>
    public static DnsAnswer Read(ReadOnlySpan<byte> message)
    {
        try
        {
            if (message.Length < 12)
                return new DnsAnswer([], 2, false);
            var truncated = (message[2] & 0x02) != 0;
            var code = message[3] & 0x0F;
            var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
            var answers = BinaryPrimitives.ReadUInt16BigEndian(message[6..]);
            var at = 12;
            for (var q = 0; q < questions; q++)
            {
                Name(message, ref at);
                at += 4;
            }
            var records = new List<DnsRecord>();
            for (var a = 0; a < answers; a++)
            {
                var name = Name(message, ref at);
                var type = BinaryPrimitives.ReadUInt16BigEndian(message[at..]);
                var ttl = BinaryPrimitives.ReadInt32BigEndian(message[(at + 4)..]);
                var length = BinaryPrimitives.ReadUInt16BigEndian(message[(at + 8)..]);
                at += 10;
                if (Enum.IsDefined((DnsType)type) && Value(message, at, length, (DnsType)type) is { } value)
                    records.Add(new DnsRecord(name, (DnsType)type, Math.Max(0, ttl), value));
                at += length;
            }
            return new DnsAnswer(records, code, truncated);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or FormatException)
        {
            return new DnsAnswer([], 2, false);
        }
    }

    /// <summary>The name to ask for from what was typed: a domain as it is, an address turned round for PTR.</summary>
    public static string? NameFor(string? typed, DnsType type)
    {
        var text = (typed ?? "").Trim().TrimEnd('.');
        if (text.Length == 0)
            return null;
        if (type == DnsType.PTR && IPAddress.TryParse(text, out var address))
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 4
                ? string.Join('.', bytes.Reverse()) + ".in-addr.arpa"
                : string.Join('.', Convert.ToHexString(bytes).ToLowerInvariant().Reverse()) + ".ip6.arpa";
        }
        // Names such as _dmarc.example.co.za and selector._domainkey.example.co.za are real names, so the stricter
        // rule for a website's domain (Domains.Of) isn't used here; a pasted address still loses its https:// and path.
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            text = text[(scheme + 3)..];
        text = text.Split('/', '?', '#')[0].ToLowerInvariant();
        if (text.Contains('@'))
            text = text[(text.LastIndexOf('@') + 1)..];
        string ascii;
        try
        {
            ascii = Ascii(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
        var labels = ascii.Split('.');
        return ascii.Length <= 253 && labels.Length >= 2
            && labels.All(l => l.Length is > 0 and <= 63 && l.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            ? ascii
            : null;
    }

    /// <summary>How long a record may be remembered, in words: "5 minutes", "1 hour", "2 days".</summary>
    public static string Lifetime(int seconds) => seconds switch
    {
        < 60 => Count(seconds, "second"),
        < 3600 => Count(seconds / 60, "minute"),
        < 172800 => Count(seconds / 3600, "hour"),
        _ => Count(seconds / 86400, "day"),
    };

    private static string Count(int number, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{number} {unit}{(number == 1 ? "" : "s")}");

    // Names with accents travel in their ASCII (punycode) form.
    private static string Ascii(string name) => name.All(char.IsAscii) ? name : new IdnMapping().GetAscii(name);

    private static string Name(ReadOnlySpan<byte> message, ref int at)
    {
        var labels = new List<string>();
        var position = at;
        var jumped = false;
        // A name may point back at an earlier one to save room; a message that points round in a circle is refused.
        for (var hops = 0; hops < 64; hops++)
        {
            var length = message[position];
            if (length == 0)
            {
                if (!jumped)
                    at = position + 1;
                return string.Join('.', labels);
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (!jumped)
                    at = position + 2;
                jumped = true;
                position = ((length & 0x3F) << 8) | message[position + 1];
                continue;
            }
            labels.Add(Encoding.ASCII.GetString(message.Slice(position + 1, length)));
            position += 1 + length;
        }
        throw new FormatException("A name in the answer points round in a circle.");
    }

    private static string? Value(ReadOnlySpan<byte> message, int at, int length, DnsType type)
    {
        var data = message.Slice(at, length);
        var inside = at;
        switch (type)
        {
            case DnsType.A when length == 4:
            case DnsType.AAAA when length == 16:
                return new IPAddress(data).ToString();
            case DnsType.NS or DnsType.CNAME or DnsType.PTR:
                return Name(message, ref inside);
            case DnsType.MX:
                inside += 2;
                return string.Create(CultureInfo.InvariantCulture, $"{BinaryPrimitives.ReadUInt16BigEndian(data)} {Name(message, ref inside)}");
            case DnsType.TXT:
                // One record can be several strings of up to 255 bytes; they read as one, joined with nothing between.
                var text = new StringBuilder();
                for (var i = 0; i < length; i += 1 + data[i])
                    text.Append(Encoding.UTF8.GetString(data.Slice(i + 1, data[i])));
                return text.ToString();
            case DnsType.SOA:
                var primary = Name(message, ref inside);
                var mailbox = Name(message, ref inside);
                var serial = BinaryPrimitives.ReadUInt32BigEndian(message[inside..]);
                return string.Create(CultureInfo.InvariantCulture, $"{primary} {mailbox} serial {serial}");
            case DnsType.SRV:
                inside += 6;
                return string.Create(CultureInfo.InvariantCulture,
                    $"{BinaryPrimitives.ReadUInt16BigEndian(data)} {BinaryPrimitives.ReadUInt16BigEndian(data[2..])} {BinaryPrimitives.ReadUInt16BigEndian(data[4..])} {Name(message, ref inside)}");
            case DnsType.CAA:
                var tag = Encoding.ASCII.GetString(data.Slice(2, data[1]));
                return string.Create(CultureInfo.InvariantCulture, $"{data[0]} {tag} \"{Encoding.ASCII.GetString(data[(2 + data[1])..])}\"");
            default:
                return null;
        }
    }
}

/// <summary>One place a name was asked at, and what it said, for the "has it spread?" check.</summary>
public sealed record DnsSighting(string Where, bool IsOwnServer, IReadOnlyList<string> Values, string? Problem = null);

/// <summary>Whether a change to a record has reached the resolvers the world asks.</summary>
public static class DnsSpread
{
    /// <summary>
    /// The domain's own name servers hold the record as it is now; everyone else remembers the old one until its
    /// time runs out. So the answer the own servers give is the one to wait for.
    /// </summary>
    public static string Verdict(IReadOnlyList<DnsSighting> sightings, int? ttlSeconds = null)
    {
        var own = sightings.Where(s => s.IsOwnServer && s.Problem is null).ToList();
        var others = sightings.Where(s => !s.IsOwnServer && s.Problem is null).ToList();
        if (others.Count == 0)
            return own.Count == 0 ? "Nobody answered. Check the internet connection." : "Only the domain's own name servers answered, so there's nothing to compare yet.";
        if (own.Count > 1 && own.Select(Key).Distinct().Count() > 1)
            return "The domain's own name servers don't agree with each other yet. Give the host a few minutes, then look again.";
        var truth = own.Count > 0 ? Key(own[0]) : null;
        if (truth is null)
        {
            return others.Select(Key).Distinct().Count() == 1
                ? "Every resolver asked gives the same answer. The domain's own name servers couldn't be asked from here, so this is the best check there is."
                : "The resolvers don't all agree, so a change is still spreading.";
        }
        var behind = others.Count(s => Key(s) != truth);
        if (behind == 0)
            return "It has spread: every resolver asked gives what the domain's own name servers give.";
        var wait = ttlSeconds is > 0 ? $" It can take up to {DnsMessage.Lifetime(ttlSeconds.Value)} more, the time the old record may be remembered." : "";
        return $"Still spreading: {behind} of {others.Count} resolvers give an older answer than the domain's own name servers.{wait}";
    }

    /// <summary>Whether this place gives what the domain's own servers give; null when there's nothing to compare with.</summary>
    public static bool? Matches(DnsSighting sighting, IReadOnlyList<DnsSighting> all)
    {
        var own = all.FirstOrDefault(s => s.IsOwnServer && s.Problem is null);
        return own is null || sighting.Problem is not null ? null : Key(sighting) == Key(own);
    }

    /// <summary>
    /// What an answer says for comparing places. A name that is another name's alias is compared by the alias:
    /// the domain's own server gives only that, while a resolver follows it to the addresses behind.
    /// </summary>
    public static IReadOnlyList<string> ValuesOf(DnsAnswer answer, DnsType type)
    {
        var alias = type == DnsType.CNAME ? null : answer.Records.FirstOrDefault(r => r.Type == DnsType.CNAME);
        return alias is null ? answer.Values(type) : ["alias of " + alias.Value];
    }

    private static string Key(DnsSighting sighting) => string.Join('\n', sighting.Values.Select(v => v.ToLowerInvariant().TrimEnd('.')).Order(StringComparer.Ordinal));
}
