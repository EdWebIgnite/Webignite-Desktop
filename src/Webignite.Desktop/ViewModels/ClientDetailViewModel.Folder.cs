using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

// The client's folder on the PC, e.g. C:\Projects\Acme. Found by name; a near match is asked about first.
public partial class ClientDetailViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder), nameof(NoFolder))]
    public partial string FolderPath { get; private set; } = "";

    /// <summary>A folder that looks like the client's, waiting for a yes.</summary>
    [ObservableProperty] public partial bool FolderIsGuess { get; private set; }

    [ObservableProperty] public partial string FolderNote { get; private set; } = "";

    public bool HasFolder => FolderPath.Length > 0;
    public bool NoFolder => !HasFolder;
    public bool CanMakeFolder => NoFolder && FileOps.ClientRoot(services.Settings.Files) is not null;

    private void LoadFolder()
    {
        var root = FileOps.ClientRoot(services.Settings.Files);
        var match = FolderOffers.Of(services, client);
        FolderPath = match.Folder ?? "";
        FolderIsGuess = match.Fit == ClientFolders.Fit.Likely;
        FolderNote = match.Fit switch
        {
            _ when !string.IsNullOrWhiteSpace(client.Folder) && !Directory.Exists(client.Folder) => "This folder isn't there any more. Pick where it went.",
            ClientFolders.Fit.Likely => "Is this their folder?",
            ClientFolders.Fit.None when root is null => "Pick their folder, or set where client folders live in Settings.",
            ClientFolders.Fit.None => $"No folder in {root} has this client's name.",
            _ => "",
        };
        OnPropertyChanged(nameof(CanMakeFolder));
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (HasFolder)
            services.Go.Folder(FolderPath);
    }

    /// <summary>Yes, that's their folder: kept, so a renamed folder or client doesn't lose it.</summary>
    [RelayCommand]
    private void ConfirmFolder()
    {
        if (HasFolder)
            UseFolder(FolderPath);
    }

    public void UseFolder(string path) => SaveRecords(c => c with { Folder = path });

    [RelayCommand]
    private void ForgetFolder() => SaveRecords(c => c with { Folder = null });

    /// <summary>A folder named after the company (or the client) in the client folders, e.g. C:\Projects\Acme; with the other direct clients' when ticked.</summary>
    [RelayCommand]
    private void MakeFolder()
    {
        if (FolderOffers.Make(services, client) is { } problem)
            FolderNote = "Couldn't make the folder: " + problem;
        else
            Refresh();
    }
}
