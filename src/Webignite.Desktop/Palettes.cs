using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Webignite.Desktop;

/// <summary>
/// Swaps the colour palette: the light one, or one of the dark ones. Every screen reads its colours by name
/// with DynamicResource, so swapping the dictionary repaints the whole app at once.
/// </summary>
public static class Palettes
{
    private static ResourceInclude? current;
    private static AppearanceSettings? applied;
    private static bool following;

    public static Uri Source(DarkPalette? dark) => new(dark is { } d
        ? $"avares://Webignite/Styles/Palette.Dark.{d}.axaml"
        : "avares://Webignite/Styles/Palette.axaml");

    /// <summary>A palette on its own, for the previews in Settings.</summary>
    public static IResourceDictionary Load(DarkPalette? dark) =>
        new ResourceInclude((Uri?)null) { Source = Source(dark) }.Loaded;

    public static bool IsDark(AppearanceSettings settings, IPlatformSettings? platform) => settings.Theme switch
    {
        Theme.Dark => true,
        Theme.FollowWindows => platform?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark,
        _ => false,
    };

    public static void Apply(AppearanceSettings settings)
    {
        if (Application.Current is not { } app)
            return;
        applied = settings;
        if (!following && app.PlatformSettings is { } platform)
        {
            following = true;
            platform.ColorValuesChanged += (_, _) =>
            {
                if (applied is { Theme: Theme.FollowWindows } again)
                    Apply(again);
            };
        }

        var dark = IsDark(settings, app.PlatformSettings);
        var source = Source(dark ? settings.DarkPalette : null);
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var merged = app.Resources.MergedDictionaries;
        if (current?.Source == source)
            return;
        // Before the first swap, the palette is the dictionary from App.axaml that holds the colour names.
        IResourceProvider? existing = (IResourceProvider?)current ?? merged.OfType<ResourceDictionary>().FirstOrDefault(d => d.ContainsKey("Canvas"));
        if (current is null && !dark)
            return;
        var next = new ResourceInclude((Uri?)null) { Source = source };
        var index = existing is null ? -1 : merged.IndexOf(existing);
        if (index >= 0)
            merged[index] = next;
        else
            merged.Insert(0, next);
        current = next;
    }
}
