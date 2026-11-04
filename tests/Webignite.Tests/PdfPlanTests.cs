using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class PdfPlanTests
{
    private static readonly IReadOnlyList<PdfPage> Five = [.. Enumerable.Range(0, 5).Select(i => new PdfPage(0, i))];

    [Fact]
    public void Pages_move_turn_and_go()
    {
        Assert.Equal([1, 2, 0, 3, 4], PdfPlan.Move(Five, 0, 2).Select(p => p.Page));
        Assert.Equal([1, 3, 0, 2, 4], PdfPlan.MoveMany(Five, [0, 2], 4).Select(p => p.Page));
        Assert.Equal([0, 2, 1, 3, 4], PdfPlan.MoveMany(Five, [2], 1).Select(p => p.Page));
        var turned = PdfPlan.Turn(Five, [1], -1);
        Assert.Equal(3, turned[1].Turns);
        Assert.Equal(0, PdfPlan.Turn(turned, [1], 1)[1].Turns);
        Assert.Equal([0, 4], PdfPlan.Remove(Five, [1, 2, 3]).Select(p => p.Page));
    }

    [Fact]
    public void Ranges_read_the_way_people_write_them()
    {
        Assert.Equal([0, 1, 2, 4], PdfPlan.Range("1-3, 5", 6).Pages);
        Assert.Equal([4, 5], PdfPlan.Range("5-", 6).Pages);
        Assert.Equal([2, 0], PdfPlan.Range("3,1", 6).Pages);
        Assert.Equal("Pick pages from 1 to 6.", PdfPlan.Range("4-9", 6).Problem);
        Assert.NotNull(PdfPlan.Range("two", 6).Problem);
        Assert.NotNull(PdfPlan.Range(" ", 6).Problem);
        Assert.Equal([[0, 1], [2, 3], [4]], PdfPlan.Every(5, 2));
    }

    [Theory]
    [InlineData(0, 0.25, 0.10, 150.0, 720.0)]
    [InlineData(1, 0.25, 0.10, 60.0, 200.0)]
    [InlineData(2, 0.25, 0.10, 450.0, 80.0)]
    [InlineData(3, 0.25, 0.10, 540.0, 600.0)]
    public void A_click_on_a_turned_page_lands_where_it_shows(int turns, double x, double y, double pdfX, double pdfY)
    {
        // A page stored 600 x 800 points.
        var (px, py) = PdfPlan.ToPdf(x, y, 600, 800, turns);
        Assert.Equal(pdfX, px, 3);
        Assert.Equal(pdfY, py, 3);
    }

    [Fact]
    public void Parts_are_named_by_their_pages()
    {
        Assert.Equal("Quote (page 2).pdf", PdfPlan.PartName("Quote.pdf", [1]));
        Assert.Equal("Quote (pages 1-3).pdf", PdfPlan.PartName("Quote.pdf", [0, 1, 2]));
        Assert.Equal("Quote (pages 1, 3).pdf", PdfPlan.PartName("Quote.pdf", [0, 2]));
    }
}
