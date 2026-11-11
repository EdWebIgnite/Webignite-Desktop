using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Webignite.Core.Chat;

namespace Webignite.Desktop.Controls;

/// <summary>A chat status as its dot's colour, the same way a project's status is shown: teal Available, yellow Away, the problem colour Busy, grey Offline.</summary>
public static class PresenceColours
{
    public static readonly IValueConverter Brush = new FuncValueConverter<PresenceState, IBrush?>(state =>
    {
        var key = state switch
        {
            PresenceState.Available => "CategoryTeal",
            PresenceState.Away => "CategoryYellow",
            PresenceState.Busy => "Attention",
            _ => "Muted",
        };
        return Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out var found) ? found as IBrush : null;
    });

    public static readonly IValueConverter Word = new FuncValueConverter<PresenceState, string>(PresenceRules.Word);
}
