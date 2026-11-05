namespace Webignite.Desktop.ViewModels;

public enum ProjectLinkKind
{
    MyTasks,
    Project,
    Templates,
}

/// <summary>
/// An entry in the sidebar's Projects group that opens part of the Projects page: My tasks, one project, or the
/// templates. They follow the Projects page wherever it sits in the sidebar; the projects listed are the ones
/// still running.
/// </summary>
public sealed class ProjectLink(ProjectLinkKind kind, string? id, string title, string icon)
    : SubLink(id, title, icon)
{
    public ProjectLinkKind Kind { get; } = kind;
}
