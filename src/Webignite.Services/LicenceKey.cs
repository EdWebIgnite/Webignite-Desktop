namespace Webignite.Services;

/// <summary>
/// The public half of the sync server's licence key, built into the app so licences can be checked offline and
/// can't be made up. Empty until the real server's key is made (docs/server-setup.md, "Licences"); while it's
/// empty, Settings says licences aren't set up and nothing is ever locked.
/// </summary>
public static class LicenceKey
{
    /// <summary>
    /// Whether this copy of the app checks licences at all. Off for now: Webignite is only used by the two of us, so
    /// there is nothing to licence. Off, the app never asks the server for a licence, Settings says nothing about
    /// one, and nothing is ever locked. Everything else is kept (the key below, the server's side, the rules in
    /// <c>Core.Accounts.Licences</c>), so switching this on is all it takes when the app is sold.
    /// </summary>
    public const bool Checked = false;

    public const string Public = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPbaYHUOvQOkFdel4cZbutTawCDcmEh8fhlbfztvZKNsljg+lk6qu5n89MpamUpkaMJMxPwDswkVmrXP2EUWs1A==";
}

/// <summary>The licence kept on this PC (setting <c>licence</c>): the signed token and when it was last fetched.</summary>
public sealed record KeptLicence(string Token, DateTimeOffset Fetched);
