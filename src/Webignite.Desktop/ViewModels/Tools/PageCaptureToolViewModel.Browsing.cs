using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;

namespace Webignite.Desktop.ViewModels;

// What a tab of the Browser adds to the page view it shares with Page to PDF or picture: back, forward, reload,
// a search when what was typed isn't an address, and the tools that act on the page it is on.
public sealed partial class PageCaptureToolViewModel
{
    /// <summary>True for the Browser tool: words typed in the address box are searched for instead of refused.</summary>
    public bool Browses { get; init; }

    /// <summary>Opens what was typed: the address, or for the Browser tool a search for the words.</summary>
    [RelayCommand]
    private void Browse()
    {
        if (browser is null || CaptureWorking)
            return;
        if (Browsing.Destination(CaptureAddress) is { } address)
            browser.Open(address);
    }

    [RelayCommand]
    private Task Back() => StepAsync(-1);

    [RelayCommand]
    private Task Forward() => StepAsync(1);

    private async Task StepAsync(int by)
    {
        if (browser is not { } open || !browserStarted || CaptureWorking)
            return;
        if (Browsing.HistoryStep(await open.DevToolsAsync("Page.getNavigationHistory", "{}"), by) is { } entry)
            await open.DevToolsAsync("Page.navigateToHistoryEntry", Browsing.GoToMessage(entry));
    }

    [RelayCommand]
    private async Task Reload()
    {
        if (browser is { } open && browserStarted && !CaptureWorking && capturePage is not null)
            await open.DevToolsAsync("Page.reload", "{}");
    }

    /// <summary>Looks the site on screen over in Site check: its domain, certificate, mail set-up.</summary>
    [RelayCommand]
    private void CheckThisSite()
    {
        if ((capturePage ?? Browsing.Destination(CaptureAddress)) is { } address)
            services.Go.SiteCheck(address.Host);
    }

    /// <summary>Freezes the screen to pick a colour off the page; its code is copied.</summary>
    [RelayCommand]
    private void PickPageColour() => services.Go.PickColour();

    /// <summary>The open page in the PC's normal browser, where saved passwords and extensions are.</summary>
    [RelayCommand]
    private void OpenOutside()
    {
        if ((capturePage ?? Browsing.Destination(CaptureAddress)) is { } address)
            AppServices.OpenInBrowser(address).Forget("Opening a page in the normal browser", tell: true);
    }
}
