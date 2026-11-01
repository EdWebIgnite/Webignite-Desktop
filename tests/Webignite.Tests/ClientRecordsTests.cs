using Webignite.Core.Clients;

namespace Webignite.Tests;

public sealed class ClientRecordsTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static Client Karoo => new() { Id = "c", Name = "Karoo Coffee Roasters" };

    [Fact]
    public void Notes_go_newest_first_and_blank_ones_are_skipped()
    {
        var client = ClientRecords.AddNote(Karoo, "Kick-off call. Wants online shop in phase two.", Today.AddDays(-3));
        client = ClientRecords.AddNote(client, "  ", Today);
        client = ClientRecords.AddNote(client, "Sent sitemap.", Today);
        Assert.Equal(["Sent sitemap.", "Kick-off call. Wants online shop in phase two."], client.Log.Select(n => n.Text));
        Assert.Single(ClientRecords.RemoveNote(client, client.Log[0].Id).Log);
        Assert.Throws<ArgumentException>(() => ClientRecords.AddNote(Karoo, new string('x', ClientRecords.MaxNote + 1), Today));
    }

    [Fact]
    public void Files_are_linked_once()
    {
        var client = ClientRecords.AddFiles(Karoo, [@"C:\OneDrive\Karoo\Brief.pdf", @"C:\OneDrive\Karoo\Contract.docx"], Today.AddDays(-1));
        client = ClientRecords.AddFiles(client, [@"c:\onedrive\karoo\brief.PDF", @"C:\OneDrive\Karoo\Logo.svg"], Today);
        Assert.Equal(["Logo.svg", "Brief.pdf", "Contract.docx"], client.Files.Select(f => f.Name));
        Assert.Equal(2, ClientRecords.RemoveFile(client, @"C:\ONEDRIVE\Karoo\Logo.svg").Files.Count);
    }

    [Theory]
    [InlineData("a.PDF", "PDF")]
    [InlineData("quote.xlsx", "Spreadsheet")]
    [InlineData("logo.ai", "Image")]
    [InlineData("thing", "File")]
    public void File_kinds(string path, string kind) => Assert.Equal(kind, ClientRecords.Kind(path));

    [Fact]
    public void Days_read_plainly()
    {
        Assert.Equal("Today", ClientRecords.Day(Today, Today));
        Assert.Equal("Yesterday", ClientRecords.Day(Today.AddDays(-1), Today));
        Assert.Equal("14 Oct", ClientRecords.Day(new DateOnly(2026, 10, 14), Today));
        Assert.Equal("3 Mar 2025", ClientRecords.Day(new DateOnly(2025, 3, 3), Today));
    }
}
