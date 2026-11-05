using CommunityToolkit.Mvvm.ComponentModel;

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
public sealed partial class ProjectLink(ProjectLinkKind kind, string? id, string title, string icon) : ObservableObject
{
    public ProjectLinkKind Kind { get; } = kind;

    /// <summary>The project's id, or null for My tasks and the templates.</summary>
    public string? Id { get; } = id;

    [ObservableProperty] public partial string Title { get; set; } = title;

    /// <summary>The key of its line icon in Styles/Icons.axaml; empty for a project, which shows a dot.</summary>
    public string Icon { get; } = icon;

    public bool IsProject => Kind == ProjectLinkKind.Project;
    public bool HasIcon => !IsProject;

}
