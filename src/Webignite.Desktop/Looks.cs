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

    /// <summary>More room, a white page, flat cards and soft filled boxes.</summary>
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
/// other style is a set of files under Styles/Looks/{style}/ that are only read when it is picked: Controls.axaml
/// over the whole app, and one per page (Mail.axaml) over that page's own styles, as a page's styles have the say
/// over the app's. The sidebar's tone swaps the palette's five sidebar colours for the page's, so every colour
/// still comes from the palette in use.
/// </summary>
public static class Looks
{
    private const string Folder = "/Styles/Looks/";

    private static readonly string[] NavKeys = ["Nav", "NavHover", "NavSelected", "NavText", "NavMuted"];

    // Pages that asked for their part of the style, held weakly so a closed page is still let go.
    private static readonly List<(WeakReference<StyledElement> View, string Page)> pages = [];

    private static StyleInclude? overlay;

    public static LookStyle Style { get; private set; }

    /// <summary>Call after <see cref="Palettes.Apply"/>: the sidebar's tone is worked out from the palette in use.</summary>
    public static void Apply(AppearanceSettings settings)
    {
        if (Application.Current is not { } app)
            return;
        if (settings.Style != Style || (overlay is null) != (settings.Style == LookStyle.Classic))
        {
            if (overlay is not null)
                app.Styles.Remove(overlay);
            overlay = null;
            Style = settings.Style;
            if (Style != LookStyle.Classic)
                app.Styles.Add(overlay = Include("Controls"));
            pages.RemoveAll(p => !p.View.TryGetTarget(out _));
            foreach (var (view, page) in pages)
                if (view.TryGetTarget(out var target))
                    Lay(target, page);
        }
        ApplySidebar(app, settings);
    }

    /// <summary>A page asks for its part of the style in use, and of any picked while it is open.</summary>
    public static void Attach(StyledElement view, string page)
    {
        pages.RemoveAll(p => !p.View.TryGetTarget(out _));
        pages.Add((new WeakReference<StyledElement>(view), page));
        Lay(view, page);
    }

    private static void Lay(StyledElement view, string page)
    {
        foreach (var old in view.Styles.OfType<StyleInclude>().Where(s => s.Source?.OriginalString.Contains(Folder, StringComparison.Ordinal) == true).ToList())
            view.Styles.Remove(old);
        if (Style != LookStyle.Classic)
            view.Styles.Add(Include(page));
    }

    private static StyleInclude Include(string file) =>
        new((Uri?)null) { Source = new Uri($"avares://Webignite{Folder}{Style}/{file}.axaml") };

    private static void ApplySidebar(Application app, AppearanceSettings settings)
    {
        foreach (var key in NavKeys)
            app.Resources.Remove(key);
        if (settings.Sidebar != SidebarTone.Light)
            return;
        // On a grey page the sidebar is the white of the cards; on Airy's white page it is the grey.
        var onWhitePage = settings.Style == LookStyle.Airy;
        Set("Nav", onWhitePage ? "Canvas" : "Surface");
        Set("NavHover", onWhitePage ? "Line" : "TableHead");
        Set("NavSelected", onWhitePage ? "Surface" : "Line");
        Set("NavText", "Ink");
        Set("NavMuted", "Muted");

        void Set(string key, string from)
        {
            if (app.TryGetResource(from, app.ActualThemeVariant, out var found) && found is IBrush brush)
                app.Resources[key] = brush;
        }
    }
}
