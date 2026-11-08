namespace Webignite.Services;

/// <summary>
/// The public half of the sync server's licence key, built into the app so licences can be checked offline and
/// can't be made up. Empty until the real server's key is made (docs/server-setup.md, "Licences"); while it's
/// empty, Settings says licences aren't set up and nothing is ever locked.
/// </summary>
public static class LicenceKey
{
    public const string Public = "";
}

/// <summary>The licence kept on this PC (setting <c>licence</c>): the signed token and when it was last fetched.</summary>
public sealed record KeptLicence(string Token, DateTimeOffset Fetched);
