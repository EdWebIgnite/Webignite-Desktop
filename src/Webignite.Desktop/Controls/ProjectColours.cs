using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Webignite.Core.Projects;

namespace Webignite.Desktop.Controls;

/// <summary>A project's colour name (Blue, Teal and so on) as its brush from the palette's Category colours.</summary>
public static class ProjectColours
{
    public static readonly IValueConverter Brush = new FuncValueConverter<string?, IBrush?>(colour =>
        Application.Current is { } app && app.TryFindResource("Category" + (colour ?? "Grey"), app.ActualThemeVariant, out var found) ? found as IBrush : null);

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
