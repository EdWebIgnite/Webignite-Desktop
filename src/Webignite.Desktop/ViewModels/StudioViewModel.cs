using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Studio;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Studio: the whole business as a picture, for a big screen. The map shows every client as a dot, bigger the
/// more is done for them, with their projects, websites and renewals circling (rules in <see cref="StudioMap"/>).
/// Demo names swaps every name for a made-up one and hides money, for showing it to people outside the studio.
/// <paramref name="presenting"/> is the copy shown full screen, which has no button to present itself.
/// </summary>
public sealed partial class StudioViewModel : ViewModelBase, IRefreshable, IDisposable
{
    private readonly AppServices services;

    public StudioViewModel(AppServices services, bool presenting = false, bool demo = false, StudioShow show = StudioShow.Map)
    {
        this.services = services;
        Presenting = presenting;
        Demo = demo;
        Show = show;
        services.Projects.Changed += WorkChanged;
        Refresh();
    }

    public bool Presenting { get; }
    public bool NotPresenting => !Presenting;

    [ObservableProperty] public partial bool Demo { get; set; }

    /// <summary>Which picture is showing: the map of clients, or the projects as lines.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMap), nameof(IsLines), nameof(Hint))]
    public partial StudioShow Show { get; set; }

    public bool IsMap => Show == StudioShow.Map;
    public bool IsLines => Show == StudioShow.Lines;

    /// <summary>What to do, shown in a corner while presenting.</summary>
    public string Hint => IsMap ? "Click a client to fly in  ·  Esc to close" : "Esc to close";

    [RelayCommand]
    private void ShowMap() => Show = StudioShow.Map;

    [RelayCommand]
    private void ShowLines() => Show = StudioShow.Lines;

    /// <summary>Each running project as a line with a stop per heading (rules in <see cref="StudioLines"/>).</summary>
    [ObservableProperty] public partial IReadOnlyList<ProjectLine> Lines { get; set; } = [];

    [ObservableProperty] public partial bool NoLines { get; set; }

    [ObservableProperty] public partial IReadOnlyList<PlacedStar> Stars { get; set; } = [];

    [ObservableProperty] public partial string Summary { get; set; } = "";

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    /// <summary>The dot that is picked, by its id; the map flies in to its client.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Picked), nameof(HasPicked), nameof(PickedKind), nameof(PickedAround), nameof(CanOpenPicked))]
    public partial string? SelectedId { get; set; }

    public Star? Picked => Stars.FirstOrDefault(p => p.Star.Id == SelectedId)?.Star;
    public bool HasPicked => Picked is not null;

    public string PickedKind => Picked?.Kind switch
    {
        StarKind.Client => "Client",
        StarKind.Project => "Project",
        StarKind.Site => "Website we watch",
        StarKind.Domain => "Domain or hosting",
        _ => "",
    };

    /// <summary>What circles the picked client, to list beside the map.</summary>
    public IReadOnlyList<Star> PickedAround =>
        Picked is { ParentId: null } client ? [.. Stars.Where(p => p.Star.ParentId == client.Id).Select(p => p.Star)] : [];

    /// <summary>With demo names on nothing opens, as the page behind it would show the real name.</summary>
    public bool CanOpenPicked => !Demo && !Presenting && Picked is { } star && star.Id != StudioMap.OwnId && star.Kind != StarKind.Domain;

    partial void OnDemoChanged(bool value) => Refresh();

    private void WorkChanged(string? projectId) => Refresh();

    public void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var stars = StudioMap.Build(services.Clients.List(), services.Projects.Projects(), services.Projects.EveryTask(), services.Documents.List(),
            services.Settings.Uptime, services.Store.ListRenewals(), today, Demo);
        Stars = StudioMap.Layout(stars);
        Lines = StudioLines.Build(services.Projects.Projects(), services.Projects.EveryHeading(), services.Projects.EveryTask(), services.Clients.List(), Demo);
        NoLines = Lines.Count == 0;
        IsEmpty = stars.Count == 0;
        Summary = string.Join("   ·   ", new[]
        {
            Counted(stars.Count(s => s.ParentId is null && s.Id != StudioMap.OwnId), "client", "clients"),
            Counted(stars.Count(s => s.Kind == StarKind.Project), "project under way", "projects under way"),
            Counted(stars.Count(s => s.Kind == StarKind.Site), "website watched", "websites watched"),
            Counted(stars.Count(s => s.Kind == StarKind.Domain), "renewal looked after", "renewals looked after"),
        }.Where(part => part.Length > 0));
        if (SelectedId is not null && Stars.All(p => p.Star.Id != SelectedId))
            SelectedId = null;
        OnPropertyChanged(nameof(Picked));
        OnPropertyChanged(nameof(PickedAround));
        OnPropertyChanged(nameof(CanOpenPicked));
    }

    private static string Counted(int n, string one, string many) => n == 0 ? "" : n == 1 ? $"1 {one}" : $"{n} {many}";

    [RelayCommand]
    private void ClearPicked() => SelectedId = null;

    [RelayCommand]
    private void Pick(Star star) => SelectedId = star.Id;

    [RelayCommand]
    private void OpenPicked()
    {
        if (Picked is { } star)
            Open(star);
    }

    /// <summary>Opens what a dot stands for: the client, the project, or the website's check.</summary>
    public void Open(Star star)
    {
        if (Demo || Presenting || star.Id == StudioMap.OwnId)
            return;
        switch (star.Kind)
        {
            case StarKind.Client when services.Clients.Get(star.Id) is { } client:
                services.Go.Client(client);
                break;
            case StarKind.Project:
                services.Go.Project(star.Id["project:".Length..]);
                break;
            case StarKind.Site:
                services.Go.SiteCheck(star.Name);
                break;
        }
    }

    /// <summary>A click on a line opens its project; not with demo names on, as the page behind shows the real ones.</summary>
    public void Open(ProjectLine line)
    {
        if (!Demo && !Presenting)
            services.Go.Project(line.ProjectId);
    }

    public void Dispose() => services.Projects.Changed -= WorkChanged;
}

/// <summary>The pictures Studio can show.</summary>
public enum StudioShow { Map, Lines }
