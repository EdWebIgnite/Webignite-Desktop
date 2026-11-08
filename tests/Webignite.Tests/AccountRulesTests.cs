using System.Text;
using Webignite.Core.Accounts;

namespace Webignite.Tests;

public sealed class AccountRulesTests
{
    // RFC 6238, appendix B (SHA-1), cut to six digits.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Codes_match_the_standard(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, Totp.Code(secret, Totp.StepOf(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
    }

    [Fact]
    public void A_code_works_once_and_a_little_either_side_of_now()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var key = Base32.Decode(secret)!;
        var code = Totp.Code(key, Totp.StepOf(now));

        var step = Totp.Verify(secret, code[..3] + " " + code[3..], now);
        Assert.Equal(Totp.StepOf(now), step);
        Assert.Null(Totp.Verify(secret, code, now, lastUsed: step!.Value));
        Assert.NotNull(Totp.Verify(secret, Totp.Code(key, Totp.StepOf(now) - 1), now));
        Assert.Null(Totp.Verify(secret, Totp.Code(key, Totp.StepOf(now) - 3), now));
        Assert.Null(Totp.Verify(secret, "12345", now));
        Assert.StartsWith("otpauth://totp/Webignite:eddie%40webignite.co.za?secret=", Totp.Uri(secret, "eddie@webignite.co.za"), StringComparison.Ordinal);
    }

    [Fact]
    public void Base32_round_trips_and_ignores_spacing()
    {
        var bytes = Encoding.ASCII.GetBytes("12345678901234567890");
        var text = Base32.Encode(bytes);
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", text);
        Assert.Equal(bytes, Base32.Decode("gezd gnbv-gy3t qojq gezd gnbv gy3t qojq"));
        Assert.Null(Base32.Decode("not base32!"));
    }

    [Fact]
    public void A_password_makes_two_separate_keys_and_too_few_rounds_are_refused()
    {
        var salt = new byte[AccountKeys.SaltBytes];
        var first = AccountKeys.Derive("correct horse battery staple", salt, AccountKeys.Rounds);
        var again = AccountKeys.Derive("correct horse battery staple", salt, AccountKeys.Rounds);
        Assert.Equal(first.SignInKey, again.SignInKey);
        Assert.Equal(first.EncryptionKey, again.EncryptionKey);
        Assert.NotEqual(Convert.FromBase64String(first.SignInKey), first.EncryptionKey);
        Assert.Throws<ArgumentOutOfRangeException>(() => AccountKeys.Derive("x", salt, 1000));
    }

    [Fact]
    public void Emails_and_passwords_are_checked()
    {
        Assert.Null(AccountRules.EmailProblem("eddie@webignite.co.za"));
        Assert.NotNull(AccountRules.EmailProblem("eddie"));
        Assert.NotNull(AccountRules.EmailProblem("ed die@webignite.co.za"));
        Assert.Equal("eddie@webignite.co.za", AccountRules.Normal("  Eddie@Webignite.co.za "));
        Assert.NotNull(AccountRules.PasswordProblem("short"));
        Assert.NotNull(AccountRules.PasswordProblem("aaaaaaaaaaaaaaaa"));
        Assert.NotNull(AccountRules.PasswordProblem("eddie-loves-coffee", "eddie@webignite.co.za"));
        Assert.Null(AccountRules.PasswordProblem("karoo rooibos sunrise", "eddie@webignite.co.za"));
    }
}
