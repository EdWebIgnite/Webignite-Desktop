using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>
/// What a page shows when it has nothing in it yet: an icon, a short heading, a line on what goes here,
/// and the button that adds the first one. The look lives in Styles/Controls.axaml.
/// </summary>
public sealed class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<EmptyState, Geometry?>(nameof(Icon));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Text));
    public static readonly StyledProperty<string?> ActionTextProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(Command));

    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
}
