using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class MarkupTests
{
    private static Mark Pin(double x, double y) => new(MarkKind.Pin, MarkColour.Red, [new(x, y)]);
    private static Mark Box(double x1, double y1, double x2, double y2) => new(MarkKind.Box, MarkColour.Red, [new(x1, y1), new(x2, y2)]);

    [Fact]
    public void Pins_are_numbered_in_order()
    {
        IReadOnlyList<Mark> marks = [];
        marks = Markup.Add(marks, Pin(10, 10));
        marks = Markup.Add(marks, Box(0, 0, 100, 50));
        marks = Markup.Add(marks, Pin(20, 20));
        Assert.Equal([1, 0, 2], marks.Select(m => m.Number));
    }

    [Fact]
    public void Tiny_drags_are_ignored()
    {
        Assert.Empty(Markup.Add([], Box(10, 10, 13, 12)));
        Assert.Single(Markup.Add([], Box(10, 10, 30, 12)));
    }

    [Fact]
    public void Removing_a_pin_renumbers_the_rest()
    {
        IReadOnlyList<Mark> marks = [];
        foreach (var x in new[] { 1, 2, 3 })
            marks = Markup.Add(marks, Pin(x * 10, 0));
        marks = Markup.SetComment(marks, 3, "Logo is blurry");
        marks = Markup.RemovePin(marks, 1);
        Assert.Equal([1, 2], marks.Select(m => m.Number));
        Assert.Equal("Logo is blurry", marks[1].Comment);
        Assert.Equal(3, Markup.NextNumber(marks));
    }

    [Fact]
    public void Undo_takes_the_last_mark()
    {
        var marks = Markup.Add(Markup.Add([], Pin(1, 1)), Box(0, 0, 50, 50));
        Assert.Equal(MarkKind.Pin, Assert.Single(Markup.Undo(marks)).Kind);
        Assert.Empty(Markup.Undo([]));
    }

    [Fact]
    public void Boxes_work_from_any_corner() =>
        Assert.Equal((10d, 20d, 90d, 30d), Markup.Box(new(100, 50), new(10, 20)));

    [Fact]
    public void Arrow_heads_point_back_along_the_line()
    {
        var (left, right) = Markup.ArrowHead(new(0, 0), new(100, 0), 20);
        Assert.True(left.X < 100 && right.X < 100);
        Assert.Equal(-left.Y, right.Y, 6);
    }

    [Fact]
    public void Feedback_lists_numbered_comments()
    {
        IReadOnlyList<Mark> marks = [];
        foreach (var x in new[] { 1, 2, 3 })
            marks = Markup.Add(marks, Pin(x, x));
        marks = Markup.SetComment(Markup.SetComment(marks, 1, " Make the phone number bigger "), 3, "Swap this photo");
        Assert.Equal("1. Make the phone number bigger\n3. Swap this photo", Markup.Feedback(marks));

        var (subject, body) = Markup.Mail("Karoo Coffee", "Thandi Mokoena", "the home page", marks, "Webignite");
        Assert.Equal("Notes on the home page", subject);
        Assert.StartsWith("Hi Thandi,\n\nI've marked up the home page in the attached screenshot. The numbers match these notes:\n\n1. Make", body);
        Assert.EndsWith("Thanks,\nWebignite", body);
    }

    [Fact]
    public void Files_are_named_by_client_and_time() =>
        Assert.Equal("karoo-coffee screenshot 2026-11-01 0930.png", Markup.FileName("Karoo Coffee", new DateTime(2026, 11, 1, 9, 30, 0)));
}
