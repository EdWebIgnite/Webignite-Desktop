using System.ComponentModel;

namespace Webignite.Desktop.ViewModels;

// The sidebar's My pages group: each page the person made, by name, and an entry to make a new one. The entry for
// the page on screen is the one marked.
public partial class MainViewModel
{
    private readonly MyPageLink newPageLink = new(null, "New page", "Plus");
    private readonly Dictionary<string, MyPageLink> myPageLinks = [];
    private MyPagesViewModel? watchedMyPages;

    /// <summary>Lists the person's pages under My pages in the sidebar, keeping the entries already there.</summary>
    private void ListMyPages()
    {
        var pages = services.Settings.MyPages;
        var links = new List<SubLink>();
        foreach (var page in pages)
        {
            if (!myPageLinks.TryGetValue(page.Id, out var link))
                myPageLinks[page.Id] = link = new MyPageLink(page.Id, page.Name, "");
            link.Title = page.Name;
            links.Add(link);
        }
        foreach (var gone in myPageLinks.Keys.Except(pages.Select(p => p.Id)).ToList())
            myPageLinks.Remove(gone);
        links.Add(newPageLink);
        Sidebar.ShowLinks(Page.MyPages, [], links);
        OnPropertyChanged(nameof(SelectedNav));
    }

    /// <summary>The sidebar entry for the page of the person's own on screen, if there is one.</summary>
    private MyPageLink? ShownMyPageLink()
    {
        if (Selected?.Key != Page.MyPages || Current?.Page is not MyPagesViewModel { Showing: { } showing })
            return null;
        var link = myPageLinks.GetValueOrDefault(showing);
        return link is not null && Sidebar.Items.Contains(link) ? link : null;
    }

    private void OpenMyPageLink(MyPageLink link)
    {
        Selected = this[Page.MyPages];
        if (Current.Page is not MyPagesViewModel pages)
            return;
        if (link.Id is null)
            pages.NewPage();
        else
            pages.OpenPage(link.Id);
    }

    /// <summary>Follows which page the My pages page on screen shows, to mark its entry.</summary>
    private void WatchMyPages(PageHost host)
    {
        if (watchedMyPages is not null)
            watchedMyPages.PropertyChanged -= MyPagesShowingChanged;
        watchedMyPages = host.Module.Key == Page.MyPages ? host.Page as MyPagesViewModel : null;
        if (watchedMyPages is not null)
            watchedMyPages.PropertyChanged += MyPagesShowingChanged;
    }

    private void MyPagesShowingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MyPagesViewModel.Showing))
            OnPropertyChanged(nameof(SelectedNav));
    }
}
