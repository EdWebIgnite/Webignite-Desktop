namespace Webignite.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("0.015", "0.02")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("12.344", "12.34")]
    public void Cents_rounds_half_away_from_zero(string input, string expected) =>
        Assert.Equal(decimal.Parse(expected), Money.Cents(decimal.Parse(input)));

    [Fact]
    public void Rand_uses_spaces_for_thousands() =>
        Assert.Equal("R 12 345.60", Money.Rand(12345.6m));

    [Fact]
    public void Rand_shows_negative_amounts() =>
        Assert.Equal("-R 50.00", Money.Rand(-50m));

    [Theory]
    [InlineData("R1 250", "1250")]
    [InlineData("1250,50", "1250.50")]
    [InlineData("1,250.50", "1250.50")]
    [InlineData(" r 99 ", "99")]
    public void Parse_reads_amounts_as_people_type_them(string text, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Money.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Parse_gives_null_for_non_numbers(string text) => Assert.Null(Money.Parse(text));
}
