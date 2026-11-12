using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Several things open at once: pages in tabs, and a page taken out into a window of its own.</summary>
public sealed class TabsAndWindowsTests
{
    [Fact]
    public Task A_page_opens_in_a_tab_of_its_own_beside_the_one_on_screen() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);
        Assert.Single(main.Tabs);
        Assert.False(main.ShowTabs);

        var mail = main.OpenInNewTab(main[Page.Mail]);

        Assert.Equal(2, main.Tabs.Count);
        Assert.True(main.ShowTabs);
        Assert.Same(mail, main.Current);
        Assert.Equal(Page.Overview, main.Tabs[0].Key);

        // The same page a second time is a second copy, not the first one again.
        var second = main.OpenInNewTab(main[Page.Mail]);
        Assert.NotSame(mail, second);
        Assert.Equal(3, main.Tabs.Count);
    });

    [Fact]
    public Task A_page_not_on_screen_goes_into_a_window_and_the_screen_stays_as_it_was() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);
        var onScreen = main.Current;

        main.OpenInWindow(main[Page.Mail]);

        var out1 = Assert.Single(main.PoppedOut);
        Assert.Equal(Page.Mail, out1.Key);
        Assert.Same(onScreen, main.Current);
        Assert.DoesNotContain(out1, main.Tabs);
    });

    [Fact]
    public Task The_page_on_screen_goes_into_a_window_and_Overview_takes_its_place() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);
        main.Selected = main[Page.Mail];
        var mail = main.Current;
        Assert.Equal(Page.Mail, mail.Key);

        main.OpenInWindow(main[Page.Mail]);

        Assert.Same(mail, Assert.Single(main.PoppedOut));
        Assert.Equal(Page.Overview, main.Current.Key); // the last tab can't leave the window empty
        Assert.Single(main.Tabs);

        // Put back, it is a tab again.
        main.DockBack(mail);
        Assert.Empty(main.PoppedOut);
        Assert.Contains(mail, main.Tabs);
    });
}
