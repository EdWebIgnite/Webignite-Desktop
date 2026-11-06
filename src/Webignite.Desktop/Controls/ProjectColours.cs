using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Webignite.Core.Projects;

namespace Webignite.Desktop.Controls;

/// <summary>A project's colour name (Red, Aqua and so on) as its brush from the palette's Project colours, and its icon's drawing.</summary>
public static class ProjectColours
{
    public static readonly IValueConverter Brush = new FuncValueConverter<string?, IBrush?>(colour =>
        Application.Current is { } app && app.TryFindResource("Project" + (colour ?? "None"), app.ActualThemeVariant, out var found) ? found as IBrush : null);

    /// <summary>An icon name from <see cref="ProjectLooks.Icons"/> as its drawing, "Project.{name}" in Styles/Icons.axaml.</summary>
    public static readonly IValueConverter Icon = new FuncValueConverter<string?, Geometry?>(icon =>
        icon is not null && Application.Current?.TryGetResource("Project." + icon, null, out var found) == true ? found as Geometry : null);

    /// <summary>A colour's or icon's name in plain words, for its tip.</summary>
    public static readonly IValueConverter ColourLabel = new FuncValueConverter<string?, string>(colour => ProjectLooks.ColourLabel(colour ?? ""));

    public static readonly IValueConverter IconLabel = new FuncValueConverter<string?, string>(icon => ProjectLooks.IconLabel(icon ?? ""));

    /// <summary>A project's status as the colour of its dot: teal on track, yellow at risk, the problem colour off track.</summary>
    public static readonly IValueConverter Health = new FuncValueConverter<ProjectHealth, IBrush?>(health =>
    {
        var key = health switch
        {
            ProjectHealth.OnTrack => "CategoryTeal",
            ProjectHealth.AtRisk => "CategoryYellow",
            ProjectHealth.OffTrack => "Attention",
            _ => "Muted",
        };
        return Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out var found) ? found as IBrush : null;
    });
}
