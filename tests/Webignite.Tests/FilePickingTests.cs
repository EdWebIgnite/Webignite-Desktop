using Webignite.Core.Files;

namespace Webignite.Tests;

/// <summary>The app's own Open and Save box: which files it shows, and the name a file is saved under.</summary>
public sealed class FilePickingTests
{
    [Fact]
    public void Patterns_pick_files_by_their_ending_whatever_the_case()
    {
        string[] pictures = ["*.png", "*.jpg"];
        Assert.True(FilePicking.Fits("Logo.PNG", pictures));
        Assert.True(FilePicking.Fits("shop front.jpg", pictures));
        Assert.False(FilePicking.Fits("brief.pdf", pictures));
        Assert.False(FilePicking.Fits("png", pictures));
        Assert.True(FilePicking.Fits("anything.docx", []));
        Assert.True(FilePicking.Fits("anything.docx", ["*.*"]));
    }

    [Fact]
    public void A_save_name_gets_the_ending_asked_for_and_loses_what_windows_refuses()
    {
        Assert.Equal("QR code.png", FilePicking.SaveName("QR code", ["*.png"]));
        Assert.Equal("QR code.png", FilePicking.SaveName(" QR code.png ", ["*.png"]));
        Assert.Equal("Quote 12 3.pdf", FilePicking.SaveName("Quote 12/3", ["*.pdf"]));
        Assert.Equal("notes.txt", FilePicking.SaveName("notes.txt", []));
        Assert.Null(FilePicking.SaveName("  ", ["*.png"]));
        Assert.Null(FilePicking.SaveName("...", ["*.png"]));
    }

    [Fact]
    public void What_a_pick_shows_is_spelled_out()
    {
        Assert.Equal("Pictures (*.png, *.jpg)", FilePicking.Describe("Pictures", ["*.png", "*.jpg"]));
        Assert.Equal("", FilePicking.Describe("Pictures", []));
    }
}
