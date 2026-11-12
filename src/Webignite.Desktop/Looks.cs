using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace Webignite.Desktop;

/// <summary>The shape of the app: spacing, corners, edges and text sizes. Colours are the palette's, whichever style.</summary>
public enum LookStyle
{
    /// <summary>The look the app has always had. Nothing is laid over it.</summary>
    Classic,

    /// <summary>More room, flat cards and soft filled boxes, on the same soft page as Classic.</summary>
    Airy,
}

/// <summary>The sidebar in the palette's own dark colours, or in the page's light ones.</summary>
public enum SidebarTone
{
    Dark,
    Light,
}

/// <summary>
/// Lays a style over the app's own. Classic is the styles in Styles/Controls.axaml and each view, untouched; any
/// other style is a set of files under Styles/Looks/{style}/ that are only read the first time it is picked:
/// Controls.axaml over the whole app, and one per page (Mail.axaml) over that page's own styles, as a page's styles
/// have the say over the app's. Every rule in those files starts with Window.look-{style}, and each window carries
/// that class only while the style is picked, so going back to Classic switches the rules off by themselves, on
/// parts already on screen too (taking a style's file away again doesn't reach the insides of controls that are
/// already drawn). Which palette colour goes where can change too (a light sidebar takes the
/// cards' colours), but every colour still comes from the palette in use.
/// </summary>
public static class Looks
{
    private const string Folder = "/Styles/Looks/";

    private static readonly string[] SwappedKeys = ["Nav", "NavHover", "NavSelected", "NavText", "NavMuted"];

    // The small text sizes of Styles/Sizes.axaml, by name, and what Airy makes them: a size up, as small grey
    // text is what tires the eyes.
    private static readonly (string Key, double Airy)[] Sizes =
    [
        ("Text10", 11), ("Text10x5", 11.5), ("Text11", 12), ("Text11x5", 12.5), ("Text12", 13), ("Text12x5", 13.5), ("Text13", 14),
    ];

    // Pages that asked for their part of the style, and the windows that are open, held weakly so each is still
    // let go when closed.
    private static readonly List<(WeakReference<StyledElement> View, string Page)> pages = [];
    private static readonly List<WeakReference<Window>> windows = [];

    static Looks() => Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Follow(window));

    public static LookStyle Style { get; private set; }

    /// <summary>Call after <see cref="Palettes.Apply"/>: the sidebar's tone is worked out from the palette in use.</summary>
    public static void Apply(AppearanceSettings settings)
    {
        if (Application.Current is not { } app)
            return;
        Style = settings.Style;
        if (Style != LookStyle.Classic && !Has(app.Styles, "Controls"))
            app.Styles.Add(Include("Controls"));
        pages.RemoveAll(p => !p.View.TryGetTarget(out _));
        foreach (var (view, page) in pages)
            if (view.TryGetTarget(out var target))
                Lay(target, page);
        windows.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var window in windows)
            if (window.TryGetTarget(out var target))
                Mark(target);
        ApplyColours(app, settings);
    }

    /// <summary>
    /// Gives something that is about to be shown in a pop-up the look pop-ups have (Styles/DockPop.axaml). The file
    /// is read the first time a pop-up opens, as none is on screen when the app starts.
    /// </summary>
    public static void PopUps(StyledElement content)
    {
        const string file = "/Styles/DockPop.axaml";
        if (!content.Styles.OfType<StyleInclude>().Any(s => s.Source?.OriginalString.EndsWith(file, StringComparison.Ordinal) == true))
            content.Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://Webignite" + file) });
    }

    /// <summary>A page asks for its part of the style in use, and of any picked while it is open.</summary>
    public static void Attach(StyledElement view, string page)
    {
        pages.RemoveAll(p => !p.View.TryGetTarget(out _));
        pages.Add((new WeakReference<StyledElement>(view), page));
        Lay(view, page);
    }

    /// <summary>A window takes the style in use, and any picked while it is open. Windows do this as they open; one
    /// that is drawn without being shown calls it itself.</summary>
    public static void Follow(Window window)
    {
        windows.RemoveAll(w => !w.TryGetTarget(out var known) || ReferenceEquals(known, window));
        windows.Add(new WeakReference<Window>(window));
        Mark(window);
    }

    private static void Mark(Window window)
    {
        foreach (var style in Enum.GetValues<LookStyle>())
            if (style != LookStyle.Classic)
                window.Classes.Set("look-" + style.ToString().ToLowerInvariant(), style == Style);
    }

    private static void Lay(StyledElement view, string page)
    {
        if (Style != LookStyle.Classic && !Has(view.Styles, page))
            view.Styles.Add(Include(page));
    }

    private static bool Has(Styles styles, string file) =>
        styles.OfType<StyleInclude>().Any(s => s.Source?.OriginalString.EndsWith($"{Folder}{Style}/{file}.axaml", StringComparison.Ordinal) == true);

    private static StyleInclude Include(string file) =>
        new((Uri?)null) { Source = new Uri($"avares://Webignite{Folder}{Style}/{file}.axaml") };

    // Which palette colour goes where, and the small text sizes: both by name, so what is on screen follows at once.
    private static void ApplyColours(Application app, AppearanceSettings settings)
    {
        foreach (var key in SwappedKeys)
            app.Resources.Remove(key);
        foreach (var (key, airy) in Sizes)
        {
            if (settings.Style == LookStyle.Airy)
                app.Resources[key] = airy;
            else
                app.Resources.Remove(key);
        }
        if (settings.Sidebar != SidebarTone.Light)
            return;
        // A light sidebar is the white of the cards, beside the page's soft grey.
        Set("Nav", Find("Surface"));
        Set("NavHover", Find("TableHead"));
        Set("NavSelected", Find("Line"));
        Set("NavText", Find("Ink"));
        Set("NavMuted", Find("Muted"));

        IBrush? Find(string key) => app.TryGetResource(key, app.ActualThemeVariant, out var found) ? found as IBrush : null;

        void Set(string key, IBrush? brush)
        {
            if (brush is not null)
                app.Resources[key] = brush;
        }
    }
}
