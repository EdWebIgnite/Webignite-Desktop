using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;

namespace Webignite.Desktop.ViewModels;

// Settings, Files, Pictures: offering Webignite to Windows for pictures, and opening Default apps to choose it.
public sealed partial class SettingsPicturesViewModel : ViewModelBase
{
    private readonly AppServices services;

    public SettingsPicturesViewModel(AppServices services)
    {
        this.services = services;
    }

    public bool CanOfferForPictures => OperatingSystem.IsWindows();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotOfferedForPictures))]
    public partial bool OfferedForPictures { get; private set; }

    public bool NotOfferedForPictures => !OfferedForPictures;

    [ObservableProperty] public partial string PicturesMessage { get; private set; } = "";

    public void LoadPictures() => OfferedForPictures = OperatingSystem.IsWindows() && Files.PictureDefault.IsOffered();

    [RelayCommand]
    private void OfferForPictures()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            Files.PictureDefault.Offer();
            OfferedForPictures = true;
            PicturesMessage = "Webignite is in Windows' Open with list for pictures. To open them with it on a double-click, choose it in Default apps.";
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            PicturesMessage = "Windows didn't allow it: " + e.Message;
        }
    }

    [RelayCommand]
    private void StopOfferingForPictures()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            Files.PictureDefault.StopOffering();
            OfferedForPictures = false;
            PicturesMessage = "Webignite is off Windows' list for pictures. Pictures open as they did before.";
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            PicturesMessage = "Windows didn't allow it: " + e.Message;
        }
    }

    [RelayCommand]
    private Task OpenDefaultApps() => AppServices.OpenInBrowser(new Uri(PictureAssociation.DefaultAppsPage));
}
