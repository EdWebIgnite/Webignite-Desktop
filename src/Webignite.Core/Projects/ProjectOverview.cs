namespace Webignite.Core.Projects;

/// <summary>A link kept on a project's Overview, as Asana's Key resources: the brief, the staging site, the shared folder.</summary>
public sealed record ProjectLink(string Id, string Title, string Url);

/// <summary>A status update on a project, as Asana's: how it's going, a few words, who wrote it and when.</summary>
public sealed record StatusUpdate(string Id, ProjectHealth Health, string Text, string By, DateTimeOffset At);

/// <summary>How far a project has come: its tasks done, still open and late, not counting subtasks.</summary>
public sealed record ProjectProgress(int Done, int Open, int Late)
{
    public int Total => Done + Open;

    /// <summary>The share done, 0 to 1; nothing to do counts as none done.</summary>
    public double Share => Total == 0 ? 0 : (double)Done / Total;
}

/// <summary>The rules for a project's Overview tab, as Asana's: description, key resources, milestones and status updates.</summary>
public static class ProjectOverview
{
    /// <summary>The project's description to edit: its formatted copy when there is one, else its plain notes.</summary>
    public static Writing.RichDocument Description(Project project) => RichNotes.Read(project.Notes, project.NotesHtml);

    /// <summary>A description written: the plain text and, only when there's formatting, the HTML.</summary>
    public static Project WithDescription(Project project, Writing.RichDocument description)
    {
        var (notes, html) = RichNotes.Write(description);
        return project with { Notes = notes, NotesHtml = html };
    }

    /// <summary>
    /// A web address as typed, made whole: "bosveld.co.za" becomes "https://bosveld.co.za". Null when it isn't a web
    /// address (no dot in the name, or another kind of link).
    /// </summary>
    public static string? Address(string typed)
    {
        var text = typed.Trim();
        if (text.Length == 0)
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        // "mailto:someone@site.co.za" would read as a site with a login in front: that's a mail address, not a page.
        if (uri.UserInfo.Length > 0 || (!uri.Host.Contains('.', StringComparison.Ordinal) && uri.Host != "localhost"))
            return null;
        return text;
    }

    /// <summary>Adds a key resource; with no title, the site's name stands in. Says what's wrong when it can't.</summary>
    public static (Project? Changed, string Problem) AddLink(Project project, string title, string url, string id)
    {
        if (Address(url) is not { } address)
            return (null, "That isn't a web address. Try something like bosveld.co.za/brief.");
        var name = title.Trim().Length > 0 ? title.Trim() : new Uri(address).Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        return (project with { Links = [.. project.Links, new ProjectLink(id, name, address)] }, "");
    }

    public static Project RemoveLink(Project project, string id) => project with { Links = [.. project.Links.Where(l => l.Id != id)] };

    /// <summary>
    /// A status update posted: it goes first, and the project's status in its header becomes the one picked, as in
    /// Asana. A status has to be picked; the words are optional.
    /// </summary>
    public static (Project? Changed, string Problem) Post(Project project, ProjectHealth health, string text, Stamp at, string id)
    {
        if (health == ProjectHealth.None)
            return (null, "Pick how it's going: on track, at risk or off track.");
        var update = new StatusUpdate(id, health, text.Trim(), at.By, at.At);
        return (project with { Health = health, Updates = [update, .. project.Updates] }, "");
    }

    /// <summary>The project's milestones, as Asana's: the soonest due first, those with no date last.</summary>
    public static IReadOnlyList<ProjectTask> Milestones(IEnumerable<ProjectTask> tasks) =>
        [.. tasks.Where(t => t.Milestone && t.Deleted is null).OrderBy(t => t.Due is null).ThenBy(t => t.Due).ThenBy(t => t.Order)];

    /// <summary>How far the project has come, counting its tasks but not their subtasks.</summary>
    public static ProjectProgress Progress(IEnumerable<ProjectTask> tasks, DateOnly today)
    {
        var top = tasks.Where(t => t.Deleted is null && t.ParentId is null).ToList();
        return new ProjectProgress(top.Count(t => t.Done), top.Count(t => !t.Done), top.Count(t => !t.Done && t.Due is { } due && due < today));
    }
}

/// <summary>Notes kept as plain text, with an HTML copy only when they're formatted: a task's or a project's description.</summary>
public static class RichNotes
{
    public static Writing.RichDocument Read(string notes, string html) =>
        html.Length > 0 ? Writing.RichHtml.Read(html, Writing.RichHtml.DataAddress)
        : notes.Trim().Length > 0 ? Writing.RichText.FromText(notes)
        : Writing.RichDocument.Empty();

    /// <summary>The plain text and, when there's any formatting, the HTML. Nothing written clears both.</summary>
    public static (string Notes, string Html) Write(Writing.RichDocument document)
    {
        var text = Writing.RichText.ToText(document).TrimEnd();
        if (text.Length == 0 && !document.Blocks.Any(b => b is not Writing.TextBlock))
            return ("", "");
        var html = Writing.RichHtml.Write(document, Writing.RichHtml.ToDataAddress);
        var plain = Writing.RichHtml.Write(Writing.RichText.FromText(text), Writing.RichHtml.ToDataAddress);
        return (text, html == plain ? "" : html);
    }
}
