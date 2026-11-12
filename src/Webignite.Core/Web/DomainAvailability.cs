using System.Text.Json;
using System.Text.RegularExpressions;

namespace Webignite.Core.Web;

public enum DomainState
{
    /// <summary>The registry itself says nobody has it.</summary>
    Free,

    /// <summary>Nothing is known of the name in DNS, so it is almost certainly free; the registrar has the last word.</summary>
    LooksFree,

    Taken,

    /// <summary>Couldn't be told either way.</summary>
    Unsure,
}

/// <summary>Whether one domain name can still be registered, and what is known about it.</summary>
public sealed record DomainCheck(string Domain, DomainState State, string Detail)
{
    public string Verdict => State switch
    {
        DomainState.Free => "Free",
        DomainState.LooksFree => "Looks free",
        DomainState.Taken => "Taken",
        _ => "Not sure",
    };

    public bool CanBeHad => State is DomainState.Free or DomainState.LooksFree;
}

/// <summary>What was found out about a name: what DNS said, and the registry's record when it publishes one.</summary>
public sealed record DomainFacts
{
    public required string Domain { get; init; }
    public required DateOnly Today { get; init; }

    /// <summary>DNS's answer code for the name's NS records: 0 when the name exists, 3 when there is no such name. Null when DNS didn't answer.</summary>
    public int? DnsStatus { get; init; }

    public bool HasNameServers { get; init; }

    /// <summary>The registry's own record (RDAP), when it has one for the name.</summary>
    public string? RegistryJson { get; init; }

    /// <summary>The registry was asked and said it has no such name. Only true for endings whose registry answers such questions.</summary>
    public bool RegistrySaysNone { get; init; }
}

/// <summary>
/// Is a domain free? A name typed without an ending is tried with each of the usual ones. The registry is asked
/// where it answers (.com, .net, .org, .africa and most others); .co.za's doesn't, so there DNS decides: a name DNS
/// has never heard of is almost certainly free, one with name servers is taken. Nothing is registered or reserved
/// by looking.
/// </summary>
public static partial class DomainAvailability
{
    /// <summary>The endings tried for a bare name, the ones sold most first.</summary>
    public static IReadOnlyList<string> Endings { get; } = ["co.za", "com", "africa", "org.za", "net", "org", "capetown", "joburg", "durban"];

    [GeneratedRegex(@"[^a-z0-9-]+")]
    private static partial Regex NotAllowed();

    /// <summary>
    /// The names to check for what was typed. "Karoo Coffee" gives karoocoffee with every ending; "karoocoffee.com"
    /// gives that one first and then the rest. Empty when nothing usable was typed.
    /// </summary>
    public static IReadOnlyList<string> Names(string? typed)
    {
        var text = (typed ?? "").Trim().ToLowerInvariant();
        foreach (var prefix in new[] { "https://", "http://", "www." })
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                text = text[prefix.Length..];
        text = text.Split('/', '?', '#')[0].Trim('.');
        var said = Endings.OrderByDescending(e => e.Length).FirstOrDefault(e => text.EndsWith("." + e, StringComparison.Ordinal));
        if (said is not null)
            text = text[..^(said.Length + 1)];
        else if (text.Contains('.'))
        {
            // An ending that isn't on the list: check exactly what was typed, then the usual ones.
            var dot = text.IndexOf('.');
            var other = text[(dot + 1)..];
            var first = Label(text[..dot]);
            return first.Length == 0 || NotAllowed().IsMatch(other.Replace(".", "", StringComparison.Ordinal)) ? [] : [$"{first}.{other}", .. Endings.Select(e => $"{first}.{e}")];
        }
        var label = Label(text);
        if (label.Length == 0)
            return [];
        return [.. Endings.OrderBy(e => e == said ? 0 : 1).Select(e => $"{label}.{e}")];
    }

    // A name as a domain allows it: letters, digits and hyphens, no spaces, not starting or ending with a hyphen.
    private static string Label(string text)
    {
        var label = NotAllowed().Replace(text.Replace(" ", "", StringComparison.Ordinal).Replace("_", "-", StringComparison.Ordinal), "").Trim('-');
        return label.Length > 63 ? label[..63].Trim('-') : label;
    }

    /// <summary>DNS's answer code and whether any name servers came back, from a DNS-over-HTTPS JSON answer.</summary>
    public static (int? Status, bool HasNameServers) Dns(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int? status = root.TryGetProperty("Status", out var s) && s.TryGetInt32(out var code) ? code : null;
            var has = root.TryGetProperty("Answer", out var answers) && answers.ValueKind == JsonValueKind.Array
                && answers.EnumerateArray().Any(a => a.TryGetProperty("type", out var type) && type.TryGetInt32(out var t) && t == 2);
            return (status, has);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    public static DomainCheck Judge(DomainFacts facts)
    {
        if (facts.RegistryJson is not null)
        {
            var line = DomainReport.RegistrationLine(facts.RegistryJson, facts.Today);
            return new(facts.Domain, DomainState.Taken, line);
        }
        if (facts.HasNameServers)
            return new(facts.Domain, DomainState.Taken, "Somebody has it: it has name servers. Its registry doesn't publish who or until when.");
        if (facts.RegistrySaysNone && facts.DnsStatus is 3 or null)
            return new(facts.Domain, DomainState.Free, "The registry has no such name, so it can be registered.");
        return facts.DnsStatus switch
        {
            3 => new(facts.Domain, DomainState.LooksFree, "DNS has never heard of it, so it is almost certainly free. The registrar confirms when you order."),
            0 => new(facts.Domain, DomainState.Unsure, "The name exists in DNS but has no name servers of its own. It may be held or reserved; check with the registrar."),
            _ => new(facts.Domain, DomainState.Unsure, "Couldn't find out. Check the internet connection and try again."),
        };
    }
}
