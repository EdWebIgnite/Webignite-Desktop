using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Picks how one of the things beside the dock is drawn, by what it is, so each builds only its own few controls
/// and not every kind with all but one hidden. The templates are named for the widgets in the view.
/// </summary>
public sealed class BarTemplates : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; } = [];

    public Control? Build(object? param) =>
        param is BarItem item && Templates.TryGetValue(item.Widget.ToString(), out var template) ? template.Build(param) : null;

    public bool Match(object? data) => data is BarItem;
}
