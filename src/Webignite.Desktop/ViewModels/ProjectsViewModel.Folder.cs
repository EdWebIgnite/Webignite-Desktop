using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

// The open project's folder on this PC, inside its client's: Clients\G3\Amos. Only for a person who has folders
// switched on in Settings, Files, and only for a project that has a client.
public partial class ProjectsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectFolder), nameof(CanMakeProjectFolder), nameof(ProjectFolderShort))]
    public partial string ProjectFolderPath { get; private set; } = "";

    /// <summary>A folder that looks like the project's, waiting for a yes.</summary>
    [ObservableProperty] public partial bool ProjectFolderIsGuess { get; private set; }

    [ObservableProperty] public partial string ProjectFolderNote { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMakeProjectFolder))]
    public partial bool ShowProjectFolder { get; private set; }

    /// <summary>The folder from the main client folder down, e.g. Clients\G3\Amos; the whole path is its tooltip.</summary>
    public string ProjectFolderShort => WorkFolders.Short(ProjectFolderPath, FileOps.ClientRoot(services.Settings.Files));

    public bool HasProjectFolder => ProjectFolderPath.Length > 0;
    public bool CanMakeProjectFolder => ShowProjectFolder && (!HasProjectFolder || ProjectFolderIsGuess);

    private void LoadProjectFolder()
    {
        ShowProjectFolder = open?.ClientId is not null && FolderOffers.On(services);
        if (!ShowProjectFolder || open is null)
        {
            ProjectFolderPath = "";
            ProjectFolderIsGuess = false;
            ProjectFolderNote = "";
            return;
        }
        var match = FolderOffers.Of(services, open);
        var root = FileOps.ClientRoot(services.Settings.Files);
        ProjectFolderIsGuess = match.Fit == ClientFolders.Fit.Likely;
        ProjectFolderPath = match.Folder ?? "";
        ProjectFolderNote = match.Fit switch
        {
            ClientFolders.Fit.Likely => "Is this its folder?",
            ClientFolders.Fit.None when FolderOffers.WhereFor(services, open) is { } where => $"None yet. It would go in {WorkFolders.Short(where, root)}.",
            _ => "",
        };
        OnPropertyChanged(nameof(CanMakeProjectFolder));
    }

    [RelayCommand]
    private void OpenProjectFolder()
    {
        if (HasProjectFolder)
            services.Go.Folder(ProjectFolderPath);
    }

    /// <summary>Yes, that's its folder: linked on this PC, so a rename doesn't lose it.</summary>
    [RelayCommand]
    private void ConfirmProjectFolder()
    {
        if (open is null || !HasProjectFolder)
            return;
        FolderOffers.Link(services, open.Id, ProjectFolderPath);
        LoadProjectFolder();
    }

    [RelayCommand]
    private void MakeProjectFolder()
    {
        if (open is { } project)
            FolderOffers.MakeAsync(services, project, () => FolderMadeFor(project.Id)).Forget("Making the project's folder", tell: true);
    }

    private void FolderMadeFor(string projectId)
    {
        if (open?.Id == projectId)
            LoadProjectFolder();
    }
}
