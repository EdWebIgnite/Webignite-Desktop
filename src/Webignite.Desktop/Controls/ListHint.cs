using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Webignite.Desktop.Controls;

/// <summary>
/// The quiet line under a short list: a hint, and a link to the next useful thing to do when there is one.
/// The rule for when it shows and what it says lives in Core (<c>ListHints</c> and each area's <c>Hint</c>).
/// The look lives in Styles/Controls.axaml.
/// </summary>
public sealed class ListHint : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ListHint, string?>(nameof(Text));
    public static readonly StyledProperty<string?> ActionTextProperty = AvaloniaProperty.Register<ListHint, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<ListHint, ICommand?>(nameof(Command));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
}
