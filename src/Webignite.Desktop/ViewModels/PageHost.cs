using CommunityToolkit.Mvvm.ComponentModel;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One open copy of a page. A page can be open more than once (two Mail tabs, two clients side by side), so
/// the sidebar entry (<see cref="Module"/>) and what is open are kept apart: each module has its main copy,
/// and more can be opened. A copy is built the first time it is shown and can be let go again when nobody
/// has looked at it for a while (<see cref="Release"/>), unless it holds open work.
/// </summary>
public sealed partial class PageHost(Module module, Func<ViewModelBase> build) : ObservableObject
{
    private ViewModelBase? page;

    public Module Module { get; } = module;
    public Page Key => Module.Key;
    /// <summary>What the tab says: what is open in the page (a client, an invoice), or the page's name.</summary>
    public string Title => page is ITabTitle { TabTitle: { Length: > 0 } title } ? title : Module.Title;
    public bool FullBleed => Module.FullBleed;

    /// <summary>Whether this is the module's own copy, which is never closed, only let go.</summary>
    public bool IsMain => ReferenceEquals(Module.Main, this);

    /// <summary>The page, built now if it hasn't been yet.</summary>
    public ViewModelBase Page
    {
        get
        {
            if (page is null)
            {
                page = build();
                page.PropertyChanged += PageChanged;
            }
            return page;
        }
    }

    // The page belongs to this copy alone, so listening to it holds nothing that would outlive both.
    private void PageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITabTitle.TabTitle))
            OnPropertyChanged(nameof(Title));
    }

    /// <summary>Whether the page has been built and not let go since.</summary>
    public bool IsBuilt => page is not null;

    /// <summary>When the page was last on screen, for letting go of pages nobody is looking at.</summary>
    public DateTimeOffset LastSeen { get; internal set; } = DateTimeOffset.Now;

    /// <summary>Lets the page go so its memory can be freed, unless it holds open work. Returns true when it went.</summary>
    public bool Release()
    {
        if (page is null || page is IHoldsWork { HoldsWork: true })
            return false;
        var old = page;
        old.PropertyChanged -= PageChanged;
        page = null;
        (old as IDisposable)?.Dispose();
        OnPropertyChanged(nameof(IsBuilt));
        OnPropertyChanged(nameof(Title));
        return true;
    }

    /// <summary>Most pages scroll inside a centred column; Mail fills the window with its own panes.</summary>
    public bool Scrolls => !FullBleed;

    // Each page is handed to only one of the two hosts, so its view is never built twice.
    public ViewModelBase? ScrollingPage => FullBleed ? null : Page;
    public ViewModelBase? FullPage => FullBleed ? Page : null;
}
