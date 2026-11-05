using Webignite.Core.Files;

namespace Webignite.Tests;

/// <summary>Offering Webignite to Windows for pictures, and pictures handed over on the command line.</summary>
public sealed class PictureAssociationTests
{
    [Fact]
    public void Windows_starts_the_launcher_that_stays_put_through_updates()
    {
        var running = Path.Combine("Webignite", "current", "Webignite.exe");
        var launcher = Path.Combine("Webignite", "Webignite.exe");
        Assert.Equal(launcher, PictureAssociation.Launcher(running, p => p == launcher));
        Assert.Equal(running, PictureAssociation.Launcher(running, _ => false));
        var dev = Path.Combine("bin", "Debug", "Webignite.exe");
        Assert.Equal(dev, PictureAssociation.Launcher(dev, _ => true));
    }

    [Fact]
    public void Webignite_is_offered_for_each_kind_of_picture_and_taken_away_cleanly()
    {
        const string exe = @"C:\Users\Eddie\AppData\Local\Webignite\Webignite.exe";
        var entries = PictureAssociation.Entries(exe);
        Assert.Contains(entries, e => e.Key.EndsWith(@"shell\open\command", StringComparison.Ordinal) && e.Value == "\"" + exe + "\" \"%1\"");
        foreach (var extension in PictureAssociation.Extensions)
        {
            Assert.Contains(entries, e => e.Key == $@"Software\Classes\{extension}\OpenWithProgids" && e.Name == PictureAssociation.ProgId);
            Assert.Contains(entries, e => e.Key.EndsWith("FileAssociations", StringComparison.Ordinal) && e.Name == extension);
            Assert.Contains(PictureAssociation.ValuesToRemove, e => e.Key == $@"Software\Classes\{extension}\OpenWithProgids");
        }
        Assert.All(entries, e => Assert.StartsWith(@"Software\", e.Key, StringComparison.Ordinal));
        Assert.Contains(PictureAssociation.ValuesToRemove, e => e.Key == @"Software\RegisteredApplications");
    }

    [Fact]
    public void A_picture_on_the_command_line_is_found_and_anything_else_ignored()
    {
        Assert.Equal(@"C:\Shots\logo.png", PictureAssociation.PictureFrom(["--veloapp-updated", @"C:\Shots\brief.pdf", @"C:\Shots\logo.png"], _ => true));
        Assert.Null(PictureAssociation.PictureFrom([@"C:\Shots\gone.png"], _ => false));
        Assert.Null(PictureAssociation.PictureFrom([], _ => true));
    }
}
