using System.Globalization;

namespace Webignite.Core.Accounts;

/// <summary>
/// What goes to a partner being invited: one message with the link to install Webignite, the invite code and the
/// few steps, ready to copy into WhatsApp or send as a mail.
/// </summary>
public static class InvitePackage
{
    /// <summary>Where Webignite's releases are published, which installed copies update from too.</summary>
    public const string Releases = "https://github.com/EdWebIgnite/Webignite-Desktop";

    /// <summary>Always the newest installer for Windows.</summary>
    public const string InstallLink = Releases + "/releases/latest/download/WebigniteDesktop-win-Setup.exe";

    public const string Subject = "Your Webignite setup";

    /// <summary>The message: install, join with the code, then the guide takes over. Signed with the inviter's name when known.</summary>
    public static string Message(string code, DateTimeOffset expires, string? from, IFormatProvider culture)
    {
        var until = expires.ToLocalTime().ToString("d MMMM", culture);
        var name = string.IsNullOrWhiteSpace(from) ? "" : "\n\n" + from.Trim();
        return $"""
            Hi,

            Here's everything to get Webignite going on your PC.

            1. Download Webignite and open the file to install it:
            {InstallLink}
            If Windows says it protected your PC, click More info, then Run anyway.

            2. When Webignite opens, click Join with an invite code.

            3. Type this invite code:
            {code}
            It works once, until {until}.

            4. Choose your own email and password. A short guide then takes you through Xero and ChatGPT, and shows you round.

            Keep this message to yourself: the code also unlocks our shared work.{name}
            """;
    }

    /// <summary>The same message for a machine reading it back, e.g. tests: culture fixed.</summary>
    public static string Message(string code, DateTimeOffset expires) => Message(code, expires, null, CultureInfo.InvariantCulture);
}
