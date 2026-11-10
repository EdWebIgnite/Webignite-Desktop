using System.Security.Cryptography;
using Webignite.Core.Accounts;

namespace Webignite.Server;

/// <summary>
/// Signs each workspace's licence with the server's licence key (WEBIGNITE_LICENCE_KEY, never in the repository).
/// The app checks it with the public half, which is built into the app. Without a key, licences aren't offered.
/// </summary>
public sealed class LicenceService(ServerStore store, TimeProvider clock, ECDsa? key) : IDisposable
{
    public bool IsSetUp => key is not null;

    /// <summary>The public half of the licence key, for the app (safe to share), or null when there's none.</summary>
    public string? PublicKey => key is null ? null : Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public Result<LicenceReply> For(string accountId)
    {
        if (key is null)
            return Outcome.Fail<LicenceReply>(StatusCodes.Status503ServiceUnavailable, "Licences aren't set up on this server yet.");
        if (store.MembershipOf(accountId) is not { } membership || store.PlanOf(membership.WorkspaceId) is not { } plan)
            return Outcome.Fail<LicenceReply>(StatusCodes.Status403Forbidden, "You're not in a workspace.");
        var licence = Licences.For(plan.WorkspaceId, plan.Plan, plan.Created, plan.PaidUntil, clock.GetUtcNow());
        return new Result<LicenceReply>(new LicenceReply(Licences.Sign(licence, key)));
    }

    /// <summary>The key from its Base64 PKCS#8 text, or null when there's none.</summary>
    public static ECDsa? KeyFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(text), out _);
        return key;
    }

    public void Dispose() => key?.Dispose();
}
