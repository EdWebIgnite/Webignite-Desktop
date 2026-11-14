using Webignite.Core.Clients;
using Webignite.Core.Files;

namespace Webignite.Tests;

public class WorkFolderTests
{
    private static readonly string Root = Path.Combine("C:", "Projects", "Clients");
    private static readonly FileSettings Settings = new();

    private static Client Client(string name, string? company = null, bool direct = false) =>
        new() { Id = Webignite.Core.Clients.Client.NewId(), Name = name, Company = company, Direct = direct };

    [Fact]
    public void A_clients_folder_is_named_after_the_company_when_there_is_one()
    {
        Assert.Equal(Path.Combine(Root, "G3"), WorkFolders.ClientPath(Client("Francois Booyse", "G3"), Root, Settings));
        Assert.Equal(Path.Combine(Root, "Modic Digital (Pty) Ltd"), WorkFolders.ClientPath(Client("Modic Digital (Pty) Ltd."), Root, Settings));
    }

    [Fact]
    public void A_direct_client_goes_with_the_other_direct_clients()
    {
        Assert.Equal(Path.Combine(Root, "Webignite", "Trimax"), WorkFolders.ClientPath(Client("Trimax", direct: true), Root, Settings));
        Assert.Equal(Path.Combine(Root, "Trimax"), WorkFolders.ClientPath(Client("Trimax", direct: true), Root, Settings with { DirectFolder = "" }));
    }

    [Fact]
    public void A_project_folder_goes_inside_its_clients()
    {
        Assert.Equal(Path.Combine(Root, "G3", "Amos website"), WorkFolders.ProjectPath(Path.Combine(Root, "G3"), "Amos website"));
        Assert.Equal(Path.Combine(Root, "G3", "Q A"), WorkFolders.ProjectPath(Path.Combine(Root, "G3"), "Q/A"));
    }

    [Fact]
    public void A_folder_named_as_the_company_is_the_clients_without_asking()
    {
        var folders = new[] { Path.Combine(Root, "G3"), Path.Combine(Root, "Modic") };
        Assert.Equal(new ClientFolders.Match(ClientFolders.Fit.Sure, folders[0]), ClientFolders.For(Client("Francois Booyse", "G3"), folders));
        Assert.Equal(ClientFolders.Fit.None, ClientFolders.For(Client("Francois Booyse"), folders).Fit);
        Assert.Equal(new ClientFolders.Match(ClientFolders.Fit.Likely, folders[1]), ClientFolders.For(Client("Modic Digital"), folders));
    }

    [Fact]
    public void A_folder_that_looks_like_the_project_is_offered_and_a_linked_one_is_sure()
    {
        var inside = new[] { Path.Combine(Root, "G3", "Amos"), Path.Combine(Root, "G3", "Kemin") };
        Assert.Equal(new ClientFolders.Match(ClientFolders.Fit.Likely, inside[0]), WorkFolders.ForProject("p1", "Amos website", Settings, inside));
        Assert.Equal(ClientFolders.Fit.None, WorkFolders.ForProject("p1", "VidaPets website", Settings, inside).Fit);
        var linked = WorkFolders.Link(Settings, "p1", inside[1]);
        Assert.Equal(new ClientFolders.Match(ClientFolders.Fit.Sure, inside[1]), WorkFolders.ForProject("p1", "Amos website", linked, inside));
        Assert.Empty(WorkFolders.Link(linked, "p1", null).ProjectFolders);
    }

    [Fact]
    public void A_rename_moves_only_a_folder_that_had_the_old_name()
    {
        var folder = Path.Combine(Root, "G3");
        Assert.Equal(Path.Combine(Root, "G3 Creative"), WorkFolders.Renamed(folder, "G3", "G3 Creative"));
        Assert.Null(WorkFolders.Renamed(folder, "Francois Booyse", "G3 Creative")); // picked by hand, named otherwise
        Assert.Null(WorkFolders.Renamed(folder, "G3", "g3"));
    }

    [Fact]
    public void Project_folders_follow_a_client_folder_that_moved()
    {
        var from = Path.Combine(Root, "G3");
        var to = Path.Combine(Root, "G3 Creative");
        var settings = WorkFolders.Link(WorkFolders.Link(Settings, "p1", Path.Combine(from, "Amos")), "p2", Path.Combine(Root, "G3X", "Other"));
        var moved = WorkFolders.Moved(settings, from, to);
        Assert.Equal(Path.Combine(to, "Amos"), moved.ProjectFolders["p1"]);
        Assert.Equal(Path.Combine(Root, "G3X", "Other"), moved.ProjectFolders["p2"]);
    }

    [Fact]
    public void Folders_are_off_until_switched_on_and_notes_show_the_short_path()
    {
        Assert.False(Settings.OfferFolders);
        Assert.Equal(Path.Combine("Clients", "G3", "Amos"), WorkFolders.Short(Path.Combine(Root, "G3", "Amos"), Root));
    }
}
