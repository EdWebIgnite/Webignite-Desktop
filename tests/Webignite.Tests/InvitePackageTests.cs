using System.Globalization;
using Webignite.Core.Accounts;

namespace Webignite.Tests;

/// <summary>The message that goes to a partner being invited.</summary>
public sealed class InvitePackageTests
{
    [Fact]
    public void The_message_has_the_install_link_the_code_until_when_and_the_steps()
    {
        var expires = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var message = InvitePackage.Message("ABCD-EFGH-JKLM-NPQR", expires, "Eddie", CultureInfo.GetCultureInfo("en-ZA"));
        Assert.Contains(InvitePackage.InstallLink, message, StringComparison.Ordinal);
        Assert.Contains("ABCD-EFGH-JKLM-NPQR", message, StringComparison.Ordinal);
        Assert.Contains("until 17 November", message, StringComparison.Ordinal);
        Assert.Contains("Join with an invite code", message, StringComparison.Ordinal);
        Assert.EndsWith("Eddie", message, StringComparison.Ordinal);
        Assert.DoesNotContain('—', message);
    }

    [Fact]
    public void The_install_link_is_always_the_newest_windows_installer()
    {
        Assert.StartsWith(InvitePackage.Releases + "/releases/latest/download/", InvitePackage.InstallLink, StringComparison.Ordinal);
        Assert.EndsWith("-win-Setup.exe", InvitePackage.InstallLink, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_name_it_ends_on_keeping_it_private()
    {
        var message = InvitePackage.Message("ABCD", DateTimeOffset.UnixEpoch.AddDays(10));
        Assert.EndsWith("shared work.", message, StringComparison.Ordinal);
    }
}
