using Webignite.Core.Clients;
using Webignite.Core.Files;
using Webignite.Core.Projects;

namespace Webignite.Desktop.Files;

/// <summary>
/// Folders for clients and projects, for a person who has switched that on in Settings, Files: a note offers to
/// make one when a client is added or a project gets its client, and to rename it when the name changes. Nothing
/// is made, moved or renamed without a click, and it is all kept on this PC, so the other person sees none of it.
/// </summary>
public static class FolderOffers
{
    public static bool On(AppServices services) => services.Settings.Files.OfferFolders && FileOps.ClientRoot(services.Settings.Files) is not null;

    public static ClientFolders.Match Of(AppServices services, Client client) =>
        ClientFolders.For(client, FileOps.ClientFolderChoices(services.Settings.Files));

    /// <summary>Where a new folder for this client would go, or null when no main client folder is set.</summary>
    public static string? WhereFor(AppServices services, Client client) =>
        FileOps.ClientRoot(services.Settings.Files) is { } root ? WorkFolders.ClientPath(client, root, services.Settings.Files) : null;

    private static string Short(AppServices services, string path) => WorkFolders.Short(path, FileOps.ClientRoot(services.Settings.Files));

    /// <summary>After a client is added: a note that makes their folder when clicked. Nothing when a folder is theirs already or looks like it.</summary>
    public static void Offer(AppServices services, Client client, Action? made = null)
    {
        if (!On(services) || Of(services, client).Fit != ClientFolders.Fit.None || WhereFor(services, client) is not { } path)
            return;
        services.ShowNote?.Invoke($"Make a folder for {client.Called}?", $"Click here to make {Short(services, path)}.", () =>
        {
            if (Make(services, client) is { } problem)
                services.ShowNote?.Invoke("Couldn't make the folder", problem, null);
            else
                made?.Invoke();
        });
    }

    /// <summary>Makes the client's folder and keeps it on the client. Returns what went wrong, or null.</summary>
    public static string? Make(AppServices services, Client client)
    {
        if (WhereFor(services, client) is not { } path)
            return "Set where client folders live in Settings, Files first.";
        try
        {
            Directory.CreateDirectory(path);
            services.Clients.Change(client.Id, c => c with { Folder = path });
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>After a client's name or company changed: a note that renames their folder to match when clicked.</summary>
    public static void OfferRename(AppServices services, Client before, Client after, Action? renamed = null)
    {
        if (!On(services) || Of(services, before) is not { Fit: ClientFolders.Fit.Sure, Folder: { } folder } || !Directory.Exists(folder)
            || WorkFolders.Renamed(folder, before.Called, after.Called) is not { } to)
            return;
        services.ShowNote?.Invoke("Rename their folder too?", $"Click here to rename {Short(services, folder)} to {Path.GetFileName(to)}.", () =>
        {
            if (Move(services, folder, to) is { } problem)
            {
                services.ShowNote?.Invoke("Couldn't rename the folder", problem, null);
                return;
            }
            services.Clients.Change(after.Id, c => string.IsNullOrWhiteSpace(c.Folder) ? c : c with { Folder = to });
            renamed?.Invoke();
        });
    }

    private static string? Move(AppServices services, string from, string to)
    {
        try
        {
            if (Directory.Exists(to))
                return $"There is a folder called {Path.GetFileName(to)} there already.";
            Directory.Move(from, to);
            services.Settings.Files = WorkFolders.Moved(services.Settings.Files, from, to);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>The client's folder when it is known for sure and still there.</summary>
    public static string? FolderOf(AppServices services, Client? client) =>
        client is not null && Of(services, client) is { Fit: ClientFolders.Fit.Sure, Folder: { } folder } && Directory.Exists(folder) ? folder : null;

    public static ClientFolders.Match Of(AppServices services, Project project)
    {
        var settings = services.Settings.Files;
        var client = project.ClientId is { } id ? services.Clients.Get(id) : null;
        var inside = FolderOf(services, client) is { } folder ? FileOps.Folders(folder) : [];
        var match = WorkFolders.ForProject(project.Id, project.Name, settings, inside);
        // A linked folder that has since been moved or deleted is looked for by name again.
        return match.Folder is { } found && !Directory.Exists(found) ? ClientFolders.Find([project.Name], inside) : match;
    }

    /// <summary>Where a new folder for this project would go, or null when its client has no folder yet.</summary>
    public static string? WhereFor(AppServices services, Project project)
    {
        var client = project.ClientId is { } id ? services.Clients.Get(id) : null;
        if (client is null)
            return null;
        var clientFolder = FolderOf(services, client) ?? WhereFor(services, client);
        return clientFolder is null ? null : WorkFolders.ProjectPath(clientFolder, project.Name);
    }

    /// <summary>After a project gets its client: a note that makes its folder when clicked. Nothing when it has one or one looks like it.</summary>
    public static void Offer(AppServices services, Project project, Action? made = null)
    {
        if (!On(services) || Of(services, project).Fit != ClientFolders.Fit.None || WhereFor(services, project) is not { } path)
            return;
        services.ShowNote?.Invoke($"Make a folder for {project.Name}?", $"Click here to make {Short(services, path)}.", () =>
            MakeAsync(services, project, made).Forget("Making the project's folder", tell: true));
    }

    /// <summary>
    /// Makes the project's folder inside its client's (making that too when it isn't there), starting it with what
    /// is in the template folder when one is set, and links it to the project on this PC.
    /// </summary>
    public static async Task MakeAsync(AppServices services, Project project, Action? made = null)
    {
        if (WhereFor(services, project) is not { } path)
        {
            services.ShowNote?.Invoke("Couldn't make the folder", "Give the project a client first, and set where client folders live in Settings, Files.", null);
            return;
        }
        var client = services.Clients.Get(project.ClientId!);
        if (client is not null && FolderOf(services, client) is null && Make(services, client) is { } problem)
        {
            services.ShowNote?.Invoke("Couldn't make the folder", problem, null);
            return;
        }
        var template = services.Settings.Files.ProjectTemplate;
        var existed = Directory.Exists(path);
        await Task.Run(() =>
        {
            Directory.CreateDirectory(path);
            if (!existed && !string.IsNullOrWhiteSpace(template) && Directory.Exists(template))
                FileOps.CopyContents(template, path);
        });
        Link(services, project.Id, path);
        made?.Invoke();
    }

    public static void Link(AppServices services, string projectId, string? folder) =>
        services.Settings.Files = WorkFolders.Link(services.Settings.Files, projectId, folder);

    /// <summary>After a project was renamed: a note that renames its folder to match when clicked.</summary>
    public static void OfferRename(AppServices services, Project before, string newName, Action? renamed = null)
    {
        if (!On(services) || Of(services, before) is not { Fit: ClientFolders.Fit.Sure, Folder: { } folder } || !Directory.Exists(folder)
            || WorkFolders.Renamed(folder, before.Name, newName) is not { } to)
            return;
        services.ShowNote?.Invoke("Rename its folder too?", $"Click here to rename {Short(services, folder)} to {Path.GetFileName(to)}.", () =>
        {
            if (Move(services, folder, to) is { } problem)
                services.ShowNote?.Invoke("Couldn't rename the folder", problem, null);
            else
                renamed?.Invoke();
        });
    }
}
