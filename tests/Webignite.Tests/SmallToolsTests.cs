using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class SmallToolsTests
{
    [Fact]
    public void Passwords_have_every_kind_asked_for()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = Passwords.Make(new PasswordOptions(12));
            Assert.Equal(12, password.Length);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => "!@#$%^&*-_=+?".Contains(c));
            Assert.DoesNotContain(password, c => "Il1O0o".Contains(c));
        }
        var plain = Passwords.Make(new PasswordOptions(30, Numbers: false, Symbols: false));
        Assert.All(plain, c => Assert.True(char.IsLetter(c)));
        Assert.Equal(Passwords.MinLength, Passwords.Make(new PasswordOptions(3)).Length);
    }

    [Fact]
    public void Password_strength_follows_length()
    {
        Assert.Equal("Weak", Passwords.Strength(Passwords.Bits(new PasswordOptions(8, false, false))));
        Assert.Equal("Strong", Passwords.Strength(Passwords.Bits(new PasswordOptions(16))));
        Assert.Equal("Very strong", Passwords.Strength(Passwords.Bits(new PasswordOptions(24))));
    }

    [Fact]
    public void Qr_codes_use_the_standard_forms()
    {
        Assert.Equal("https://webignite.co.za", QrText.Build(QrKind.Link, " webignite.co.za "));
        Assert.Equal("http://x.co.za", QrText.Build(QrKind.Link, "http://x.co.za"));
        Assert.Equal(@"WIFI:T:WPA;S:Karoo\;Guest;P:p\:ss\\word;;", QrText.Build(QrKind.Wifi, "Karoo;Guest", @"p:ss\word"));
        Assert.Equal("WIFI:T:nopass;S:Cafe;;", QrText.Build(QrKind.Wifi, "Cafe", null, WifiSecurity.Open));
        Assert.Equal("tel:+27825550142", QrText.Build(QrKind.Phone, "082 555 0142"));
        Assert.Equal("tel:+27215550000", QrText.Build(QrKind.Phone, "27 21 555 0000"));
        Assert.Equal("mailto:hi@karoocoffee.co.za", QrText.Build(QrKind.Email, "hi@karoocoffee.co.za"));
    }

    [Fact]
    public void Qr_problems_are_explained()
    {
        Assert.Equal("Type the web address.", QrText.Problem(QrKind.Link, " "));
        Assert.Equal("Type the Wi-Fi password, or choose an open network.", QrText.Problem(QrKind.Wifi, "Cafe"));
        Assert.Null(QrText.Problem(QrKind.Wifi, "Cafe", null, WifiSecurity.Open));
        Assert.Equal("That doesn't look like an email address.", QrText.Problem(QrKind.Email, "hello"));
    }

    [Theory]
    [InlineData("#1c1d21", "#1C1D21")]
    [InlineData("1C1D21", "#1C1D21")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("rgb(28, 29, 33)", "#1C1D21")]
    public void Colours_read_the_usual_forms(string text, string hex) => Assert.Equal(hex, Colours.Parse(text)!.Value.Hex);

    [Theory]
    [InlineData("#12345")]
    [InlineData("rgb(300, 0, 0)")]
    [InlineData("blue")]
    public void Bad_colours_are_refused(string text) => Assert.Null(Colours.Parse(text));

    [Fact]
    public void Colours_convert_and_compare()
    {
        var orange = Colours.Parse("#E8590C")!.Value;
        Assert.Equal("rgb(232, 89, 12)", orange.Css);
        Assert.Equal("hsl(21, 90%, 48%)", orange.Hsl);
        var (h, s, l) = Colours.ToHsl(orange);
        Assert.Equal(orange, Colours.FromHsl(h, s, l));
        Assert.Equal(21, Colours.Contrast(new Rgb(0, 0, 0), new Rgb(255, 255, 255)), 3);
        Assert.Equal("Fine for normal text", Colours.Readability(Colours.Contrast(new Rgb(0x76, 0x76, 0x76), new Rgb(255, 255, 255))));
        Assert.Equal(6, Colours.Shades(orange).Count);
    }
}
