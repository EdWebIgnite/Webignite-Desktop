using CommunityToolkit.Mvvm.ComponentModel;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One entry in the sidebar. The app is a shell of modules, so Mail and later tools slot in as
/// another entry without touching the rest.
/// </summary>
public sealed partial class Module(string title, ViewModelBase page, string icon, bool isLater = false, bool fullBleed = false) : ObservableObject
{
    public string Title { get; } = title;

    /// <summary>The key of its line icon in Styles/Icons.axaml, without the "Icon." in front.</summary>
    public string Icon { get; } = icon;
    public ViewModelBase Page { get; } = page;
    public bool IsLater { get; } = isLater;
    public bool FullBleed { get; } = fullBleed;

    /// <summary>A count shown beside the name, e.g. unread mail. Zero hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    public partial int Badge { get; set; }

    public bool HasBadge => Badge > 0;

    /// <summary>Most pages scroll inside a centred column; Mail fills the window with its own panes.</summary>
    public bool Scrolls => !FullBleed;

    // Each page is handed to only one of the two hosts, so its view is never built twice.
    public ViewModelBase? ScrollingPage => FullBleed ? null : Page;
    public ViewModelBase? FullPage => FullBleed ? Page : null;
}

/// <summary>A small heading between groups of pages in the sidebar. It can't be picked.</summary>
public sealed record NavHeading(string Title);
