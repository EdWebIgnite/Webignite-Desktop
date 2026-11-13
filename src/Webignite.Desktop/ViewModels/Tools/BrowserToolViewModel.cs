using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Browser: tabs down the side, a slim address bar with the web tools at its end as a browser has its
/// extensions, and a device viewer that shows the page inside a phone, tablet or computer screen. Each tab is a
/// page view of its own (<see cref="PageCaptureToolViewModel"/>) with its own browser engine, started when the
/// tab is shown and shut down when the tab or the window closes, so nothing of the browser outlives the window.
/// </summary>
public sealed partial class BrowserToolViewModel : ViewModelBase, IDisposable
{
    /// <summary>The most tabs open at once: each holds a browser page in memory.</summary>
    public const int MostTabs = 12;

    private readonly AppServices services;

    public BrowserToolViewModel(AppServices services)
    {
        this.services = services;
        DeviceGroups =
        [
            new("Phones", [.. Devices.All.Where(d => d.Kind == DeviceKind.Phone).Select(d => new DeviceTile(d, this))]),
            new("Tablets", [.. Devices.All.Where(d => d.Kind == DeviceKind.Tablet).Select(d => new DeviceTile(d, this))]),
            new("Computers", [.. Devices.All.Where(d => d.Kind == DeviceKind.Computer).Select(d => new DeviceTile(d, this))]),
        ];
        Current = Add();
    }

    public ObservableCollection<BrowserTab> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Page))]
    public partial BrowserTab Current { get; private set; }

    /// <summary>The page of the tab on screen: what the address bar and the tools act on.</summary>
    public PageCaptureToolViewModel Page => Current.Page;

    public bool CanAddTab => Tabs.Count < MostTabs;

    /// <summary>The device panel is open beside the page.</summary>
    [ObservableProperty] public partial bool ShowsDevices { get; set; }

    public IReadOnlyList<DeviceGroup> DeviceGroups { get; }

    /// <summary>The name of the device the page on screen is shown as; empty when it fills the window.</summary>
    public string DeviceName => Page.CaptureDevice is { Device: { } device } ? device.Name : "";

    private BrowserTab Add()
    {
        var tab = new BrowserTab(new PageCaptureToolViewModel(services) { Browses = true });
        tab.Page.PropertyChanged += PageChanged;
        Tabs.Add(tab);
        OnPropertyChanged(nameof(CanAddTab));
        return tab;
    }

    private void PageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Page) && e.PropertyName is nameof(PageCaptureToolViewModel.CaptureDevice))
            DevicesChanged();
    }

    partial void OnCurrentChanged(BrowserTab oldValue, BrowserTab newValue)
    {
        // There is no tab before the first one.
        if (oldValue is { } was)
            was.IsCurrent = false;
        newValue.IsCurrent = true;
        DevicesChanged();
    }

    private void DevicesChanged()
    {
        OnPropertyChanged(nameof(DeviceName));
        foreach (var tile in DeviceGroups.SelectMany(g => g.Tiles))
            tile.Refresh();
    }

    [RelayCommand]
    private void NewTab()
    {
        if (CanAddTab)
            Current = Add();
    }

    [RelayCommand]
    private void Select(BrowserTab? tab)
    {
        if (tab is not null && Tabs.Contains(tab))
            Current = tab;
    }

    /// <summary>Closes a tab and shuts its page's browser down. The last tab closing leaves a fresh empty one.</summary>
    [RelayCommand]
    private void CloseTab(BrowserTab? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
            return;
        var at = Tabs.IndexOf(tab);
        if (Tabs.Count == 1)
            Current = Add();
        else if (ReferenceEquals(tab, Current))
            Current = Tabs[at == Tabs.Count - 1 ? at - 1 : at + 1];
        Tabs.Remove(tab);
        tab.Page.PropertyChanged -= PageChanged;
        tab.Page.Dispose();
        OnPropertyChanged(nameof(CanAddTab));
    }

    /// <summary>
    /// Opens or closes the device viewer. Opening it with the page filling the window shows it as a phone straight
    /// away, since that is what the viewer is for.
    /// </summary>
    [RelayCommand]
    private void ToggleDevices()
    {
        ShowsDevices = !ShowsDevices;
        if (ShowsDevices && !Page.IsDeviceOn)
            Show(Devices.All.First(d => d.Name == "iPhone 14"));
    }

    /// <summary>Shows the page on screen as a device.</summary>
    public void Show(Device device) => Page.CaptureDevice = Page.CaptureDevices.First(c => ReferenceEquals(c.Device, device));

    /// <summary>Back to the page filling the window, and the device panel put away.</summary>
    [RelayCommand]
    private void LeaveDevices()
    {
        Page.CaptureDevice = Page.CaptureDevices[0];
        ShowsDevices = false;
    }

    [RelayCommand]
    private void Rotate() => Page.CaptureSideways = !Page.CaptureSideways;

    public void Dispose()
    {
        foreach (var tab in Tabs)
        {
            tab.Page.PropertyChanged -= PageChanged;
            tab.Page.Dispose();
        }
    }
}

/// <summary>One tab of the browser: a page, and its name down the side.</summary>
public sealed partial class BrowserTab : ObservableObject
{
    public BrowserTab(PageCaptureToolViewModel page)
    {
        Page = page;
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PageCaptureToolViewModel.PageTitle) or nameof(PageCaptureToolViewModel.CaptureAddress))
                OnPropertyChanged(nameof(Title));
        };
    }

    public PageCaptureToolViewModel Page { get; }

    /// <summary>The page's own name; while it loads, its address; "New tab" before anything is opened.</summary>
    public string Title => Page.PageTitle.Length > 0 ? Page.PageTitle : Page.CaptureAddress.Trim().Length > 0 ? Page.CaptureAddress.Trim() : "New tab";

    [ObservableProperty] public partial bool IsCurrent { get; set; }
}

/// <summary>The devices of one kind in the device panel.</summary>
public sealed record DeviceGroup(string Name, IReadOnlyList<DeviceTile> Tiles);

/// <summary>A device to show the page as: its name and screen size, lit when it is the one on.</summary>
public sealed partial class DeviceTile(Device device, BrowserToolViewModel browser) : ObservableObject
{
    public string Name => device.Name;
    public string Size => $"{device.Width} × {device.Height}";
    public bool IsPhone => device.Kind == DeviceKind.Phone;
    public bool IsTablet => device.Kind == DeviceKind.Tablet;
    public bool IsComputer => device.Kind == DeviceKind.Computer;
    public bool IsOn => ReferenceEquals(browser.Page.CaptureDevice?.Device, device);

    internal void Refresh() => OnPropertyChanged(nameof(IsOn));

    [RelayCommand]
    private void Pick() => browser.Show(device);
}
