namespace Webignite.Tests;

public class LineTests
{
    [Fact]
    public void Amount_is_quantity_times_price() =>
        Assert.Equal(1350.00m, Samples.Priced("Design", 3, 450m).Amount());

    [Fact]
    public void Percentage_discount_applies_before_rounding() =>
        Assert.Equal(33.33m, (Samples.Priced("Hours", 1, 37.03m) with { DiscountPercent = 10 }).Amount());

    [Fact]
    public void Amount_discount_is_subtracted() =>
        Assert.Equal(400m, (Samples.Priced("Logo", 1, 450m) with { DiscountAmount = 50 }).Amount());

    [Fact]
    public void Text_only_line_is_zero() =>
        Assert.Equal(0m, new Line { Description = "Thank you for your business" }.Amount());
}
