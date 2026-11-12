using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;

namespace Webignite.Desktop.ViewModels;

// The Browser tool: the same page view as Page to PDF or picture, used for looking at a site and trying it at
// phone, tablet and computer sizes. It adds back, forward, reload, a search when what was typed isn't an address,
// and one-click sizes. Like the other tool it starts the browser when its window opens and shuts it down on close.
public sealed partial class PageCaptureToolViewModel
{
    /// <summary>True for the Browser tool: words typed in the address box are searched for instead of refused.</summary>
    public bool Browses { get; init; }

    private ObservableCollection<QuickSize>? quickSizes;

    /// <summary>The sizes one click away: the window's own, a phone, a tablet, a laptop and a desktop screen.</summary>
    public ObservableCollection<QuickSize> QuickSizes => quickSizes ??= MakeQuickSizes();

    private ObservableCollection<QuickSize> MakeQuickSizes()
    {
        DeviceChoice Of(string device) => CaptureDevices.First(d => d.Label == device);
        ObservableCollection<QuickSize> sizes =
        [
            new("Fit window", CaptureDevices[0], this),
            new("Phone", Of("iPhone 14"), this),
            new("Small phone", Of("Budget Android (360 wide)"), this),
            new("Tablet", Of("iPad Air"), this),
            new("Laptop", Of("Laptop"), this),
            new("Desktop", Of("Desktop"), this),
        ];
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CaptureDevice))
                foreach (var size in sizes)
                    size.Refresh();
        };
        return sizes;
    }

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

    /// <summary>The open page in the PC's normal browser, where saved passwords and extensions are.</summary>
    [RelayCommand]
    private void OpenOutside()
    {
        if ((capturePage ?? Browsing.Destination(CaptureAddress)) is { } address)
            AppServices.OpenInBrowser(address).Forget("Opening a page in the normal browser", tell: true);
    }
}

/// <summary>One of the Browser tool's one-click sizes.</summary>
public sealed partial class QuickSize(string label, DeviceChoice choice, PageCaptureToolViewModel tool) : ObservableObject
{
    public string Label => label;
    public string Tip => choice.Device is { } d ? $"{d.Name}, {d.Width} × {d.Height}" : "As wide as this window";
    public bool IsOn => ReferenceEquals(tool.CaptureDevice, choice) || (tool.CaptureDevice is null && choice.Device is null && !choice.Custom);
    public bool IsOff => !IsOn;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(IsOff));
    }

    [RelayCommand]
    private void Pick() => tool.CaptureDevice = choice;
}
