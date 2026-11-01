namespace Webignite.Tests;

public sealed class PriceListTests
{
    private static readonly Line[] Website =
    [
        Samples.Priced("Design and build, 6 pages", 1, 12500m),
        Samples.Priced("Hosting, first year", 1, 2000m),
    ];

    [Fact]
    public void Saving_makes_a_package_with_a_total()
    {
        var list = PriceList.Save([], " Website, 6 pages ", Website);
        var entry = Assert.Single(list);
        Assert.Equal("Website, 6 pages", entry.Name);
        Assert.Equal(14500m, entry.Total);
    }

    [Fact]
    public void Saving_under_the_same_name_updates_it()
    {
        var list = PriceList.Save([], "Website, 6 pages", Website);
        var id = list[0].Id;
        list = PriceList.Save(list, "WEBSITE, 6 PAGES", [Samples.Priced("Design and build, 6 pages", 1, 13500m)]);
        var entry = Assert.Single(list);
        Assert.Equal(id, entry.Id);
        Assert.Equal(13500m, entry.Total);
    }

    [Fact]
    public void Sorted_by_name()
    {
        var list = PriceList.Save(PriceList.Save([], "SEO, monthly", Website), "Hosting", Website);
        Assert.Equal(["Hosting", "SEO, monthly"], list.Select(e => e.Name));
    }

    [Fact]
    public void Needs_a_name_and_lines()
    {
        Assert.Throws<ArgumentException>(() => PriceList.Save([], " ", Website));
        Assert.Throws<ArgumentException>(() => PriceList.Save([], "Empty", [new Line { Description = " " }]));
    }

    [Fact]
    public void Adding_drops_the_blank_first_line()
    {
        var entry = PriceList.Save([], "Website", Website)[0];
        var lines = PriceList.AddTo([null, new Line { Description = "" }], entry);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Design and build, 6 pages", lines[0].Description);
    }

    [Fact]
    public void Adding_keeps_lines_already_there()
    {
        var entry = PriceList.Save([], "Website", Website)[0];
        var lines = PriceList.AddTo([Samples.Priced("Logo refresh", 1, 3200m)], entry);
        Assert.Equal(["Logo refresh", "Design and build, 6 pages", "Hosting, first year"], lines.Select(l => l.Description));
    }

    [Fact]
    public void Removing()
    {
        var list = PriceList.Save([], "Website", Website);
        Assert.Empty(PriceList.Remove(list, list[0].Id));
    }
}
