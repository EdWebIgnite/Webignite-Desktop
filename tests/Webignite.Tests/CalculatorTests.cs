using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class CalculatorTests
{
    [Theory]
    [InlineData("3.5 x 650", "2275")]
    [InlineData("3,5 * 650", "2275")]
    [InlineData("R1 200 + 15%", "1380")]
    [InlineData("2400 - 10%", "2160")]
    [InlineData("(2 400 - 300) / 12", "175")]
    [InlineData("1 250 000 / 4", "312500")]
    [InlineData("15%", "0.15")]
    [InlineData("200 * 15%", "30")]
    [InlineData("-50 + 20", "-30")]
    [InlineData("2 + 3 × 4", "14")]
    [InlineData("10 ÷ 4", "2.5")]
    [InlineData("0.1 + 0.2", "0.3")]
    public void Sums_work_out(string sum, string expected) =>
        Assert.Equal(expected, Calculator.Plain(Calculator.Evaluate(sum).Value!.Value));

    [Theory]
    [InlineData("", "Type a sum.")]
    [InlineData("5 / 0", "Can't divide by zero.")]
    [InlineData("(5 + 2", "A bracket isn't closed.")]
    [InlineData("5 +", "The sum stops too early.")]
    [InlineData("5 $ 2", "\"$\" can't be used in a sum.")]
    [InlineData("5 * / 2", "Check the sum: two signs are next to each other.")]
    public void Problems_are_explained(string sum, string problem)
    {
        var result = Calculator.Evaluate(sum);
        Assert.False(result.Ok);
        Assert.Equal(problem, result.Problem);
    }

    [Fact]
    public void Price_boxes_take_numbers_or_sums()
    {
        Assert.Equal(1250.5m, Calculator.Amount("R1 250,50"));
        Assert.Equal(1950m, Calculator.Amount("3 x 650"));
        Assert.Equal(-20m, Calculator.Amount("-20"));
        Assert.Null(Calculator.Amount("call me"));
        Assert.False(Calculator.LooksLikeSum("-20"));
        Assert.True(Calculator.LooksLikeSum("3x650"));
    }
}
