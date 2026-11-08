using System.Security.Cryptography;
using System.Text.Json;

namespace Webignite.Core.Accounts;

/// <summary>What a workspace pays for: a free trial, a paid subscription, or free for good (Webignite's own).</summary>
public static class LicencePlans
{
    public const string Trial = "trial";
    public const string Active = "active";
    public const string Complimentary = "complimentary";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Trial, Active, Complimentary };
}

/// <summary>A workspace's licence as the server signs it: which plan, and until when it's good.</summary>
public sealed record Licence(string WorkspaceId, string Plan, DateTimeOffset ValidUntil, DateTimeOffset Issued);

public enum LicenceState { None, Trial, Active, Grace, Locked }

/// <summary>Where a licence stands on a day, for the app: still fine, in its grace days, or run out.</summary>
public sealed record LicenceStanding(LicenceState State, int DaysLeft, string Text);

/// <summary>
/// Licences, signed by the sync server with its own key (ECDSA P-256) and checked by the app with the matching
/// public key, so they work offline and can't be made up or stretched on a PC. A new workspace gets a 30-day
/// trial; after a licence runs out there are 14 days' grace before the app locks.
/// </summary>
public static class Licences
{
    public static readonly TimeSpan TrialLength = TimeSpan.FromDays(30);
    public static readonly TimeSpan Grace = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A licence as a token: its JSON and its signature, each Base64, joined by a dot.</summary>
    public static string Sign(Licence licence, ECDsa key)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(licence, Json);
        var signature = key.SignData(body, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(body) + "." + Convert.ToBase64String(signature);
    }

    /// <summary>The licence in a token, or null when it wasn't signed with the key that goes with this public key.</summary>
    public static Licence? Read(string? token, string publicKey)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Split('.') is not [var bodyText, var signatureText] || string.IsNullOrWhiteSpace(publicKey))
            return null;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            var body = Convert.FromBase64String(bodyText);
            return key.VerifyData(body, Convert.FromBase64String(signatureText), HashAlgorithmName.SHA256)
                ? JsonSerializer.Deserialize<Licence>(body, Json)
                : null;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A new key pair for a server: the private half (PKCS#8) for the server's settings, the public half for the app.</summary>
    public static (string PrivateKey, string PublicKey) NewKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportPkcs8PrivateKey()), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>The licence a workspace has now, from its plan and dates on the server.</summary>
    public static Licence For(string workspaceId, string plan, DateTimeOffset created, DateTimeOffset? paidUntil, DateTimeOffset now) => plan switch
    {
        LicencePlans.Complimentary => new(workspaceId, plan, now.AddYears(10), now),
        LicencePlans.Active => new(workspaceId, plan, paidUntil ?? now, now),
        _ => new(workspaceId, LicencePlans.Trial, created + TrialLength, now),
    };

    /// <summary>Where a licence stands now, with the words for Settings.</summary>
    public static LicenceStanding Standing(Licence? licence, DateTimeOffset now)
    {
        if (licence is null)
            return new(LicenceState.None, 0, "No licence yet. Sign in under Account and sync to start the 30-day trial.");
        var left = DaysBetween(now, licence.ValidUntil);
        if (licence.Plan == LicencePlans.Complimentary)
            return new(LicenceState.Active, left, "Licensed, free of charge.");
        if (now < licence.ValidUntil)
            return licence.Plan == LicencePlans.Trial
                ? new(LicenceState.Trial, left, left == 1 ? "Free trial: 1 day left." : $"Free trial: {left} days left.")
                : new(LicenceState.Active, left, $"Subscription paid until {licence.ValidUntil.ToLocalTime():d MMMM yyyy}.");
        var graceLeft = DaysBetween(now, licence.ValidUntil + Grace);
        return now < licence.ValidUntil + Grace
            ? new(LicenceState.Grace, graceLeft, (licence.Plan == LicencePlans.Trial ? "The free trial has ended." : "The subscription has run out.")
                + (graceLeft == 1 ? " 1 day left before Webignite locks." : $" {graceLeft} days left before Webignite locks."))
            : new(LicenceState.Locked, 0, "The licence has run out. Renew to carry on using Webignite.");
    }

    private static int DaysBetween(DateTimeOffset from, DateTimeOffset to) => Math.Max(0, (int)Math.Ceiling((to - from).TotalDays));

    /// <summary>Whether a workspace's licence should lock the app. Off until payments are set up (see docs).</summary>
    public static bool Locks(LicenceStanding standing, bool enforced) => enforced && standing.State == LicenceState.Locked;
}

/// <summary>The licence as the server sends it: the signed token, for the app to check and keep.</summary>
public sealed record LicenceReply(string Token);
