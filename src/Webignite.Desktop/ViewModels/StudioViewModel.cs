using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Studio;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Studio: the whole business as a picture, for a big screen. The map shows every client as a dot, bigger the
/// more is done for them, with their projects, websites and renewals circling (rules in <see cref="StudioMap"/>).
/// Demo shows a made-up studio in place of the real one, for showing it to people outside the studio or while
/// the real work is still being loaded in.
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

    /// <summary>Which picture is showing: the map of clients, the wall of client cards, or the projects as lines.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMap), nameof(IsWall), nameof(IsLines), nameof(Hint))]
    public partial StudioShow Show { get; set; }

    public bool IsMap => Show == StudioShow.Map;
    public bool IsWall => Show == StudioShow.Wall;
    public bool IsLines => Show == StudioShow.Lines;

    /// <summary>What to do, shown in a corner while presenting.</summary>
    public string Hint => IsMap ? "Click a client to fly in  ·  Esc to close" : IsWall ? "Point at a card to turn it over  ·  Esc to close" : "Esc to close";

    [RelayCommand]
    private void ShowMap() => Show = StudioShow.Map;

    [RelayCommand]
    private void ShowWall() => Show = StudioShow.Wall;

    [RelayCommand]
    private void ShowLines() => Show = StudioShow.Lines;

    /// <summary>A card for every client, made from the same dots as the map (rules in <see cref="StudioWall"/>).</summary>
    [ObservableProperty] public partial IReadOnlyList<WallCard> Wall { get; set; } = [];

    /// <summary>Each running project as a line with a stop per heading (rules in <see cref="StudioLines"/>).</summary>
    [ObservableProperty] public partial IReadOnlyList<ProjectLine> Lines { get; set; } = [];

    [ObservableProperty] public partial bool NoLines { get; set; }

    [ObservableProperty] public partial IReadOnlyList<PlacedStar> Stars { get; set; } = [];

    [ObservableProperty] public partial string Summary { get; set; } = "";

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    /// <summary>The dot that is picked, by its id; the map flies in to its client.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Picked), nameof(HasPicked), nameof(PickedKind), nameof(PickedAround), nameof(CanOpenPicked), nameof(CanHidePicked))]
    public partial string? SelectedId { get; set; }

    public Star? Picked => Stars.FirstOrDefault(p => p.Star.Id == SelectedId)?.Star;
    public bool HasPicked => Picked is not null;

    public string PickedKind => Picked?.Kind switch
    {
        StarKind.Client => "Client",
        StarKind.Project => "Project",
        StarKind.Site => "Website we watch",
        StarKind.Domain => "Domain or hosting",
        StarKind.Quote => "Quote that is out",
        _ => "",
    };

    /// <summary>What circles the picked client, to list beside the map.</summary>
    public IReadOnlyList<Star> PickedAround =>
        Picked is { ParentId: null } client ? [.. Stars.Where(p => p.Star.ParentId == client.Id).Select(p => p.Star)] : [];

    /// <summary>In the demo studio nothing opens, as there is nothing real behind it.</summary>
    public bool CanOpenPicked => !Demo && !Presenting && Picked is { } star && star.Id != StudioMap.OwnId && star.Kind is not (StarKind.Domain or StarKind.Quote);

    partial void OnDemoChanged(bool value) => Refresh();

    private void WorkChanged(string? projectId) => Refresh();

    public void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var hidden = services.Settings.StudioHidden;
        var stars = Demo
            ? StudioDemo.Stars(today)
            : StudioMap.Build(services.Clients.List(), services.Projects.Projects(), services.Projects.EveryTask(), services.Documents.List(),
                services.Settings.Uptime, services.Store.ListRenewals(), today, hidden);
        var places = services.Settings.StudioPlaces;
        Stars = StudioMap.WithPlaces(StudioMap.Layout(stars), places);
        HasMoved = !Presenting && stars.Any(star => star.ParentId is null && places.ContainsKey(star.Id));
        Wall = StudioWall.Build(stars);
        Lines = Demo
            ? StudioDemo.Lines()
            : StudioLines.Build(services.Projects.Projects(), services.Projects.EveryHeading(), services.Projects.EveryTask(), services.Clients.List());
        NoLines = Lines.Count == 0;
        IsEmpty = stars.Count == 0;
        HiddenCount = Demo ? 0 : hidden.Count(id => services.Clients.Get(id) is not null);
        Summary = string.Join("   ·   ", new[]
        {
            Counted(stars.Count(s => s.ParentId is null && s.Id != StudioMap.OwnId), "client", "clients"),
            Counted(stars.Count(s => s.Kind == StarKind.Project), "project under way", "projects under way"),
            Counted(stars.Count(s => s.Kind == StarKind.Site), "website watched", "websites watched"),
            Counted(stars.Count(s => s.Kind == StarKind.Domain), "renewal looked after", "renewals looked after"),
            Counted(stars.Count(s => s.Kind == StarKind.Quote), "quote out", "quotes out"),
        }.Where(part => part.Length > 0));
        if (SelectedId is not null && Stars.All(p => p.Star.Id != SelectedId))
            SelectedId = null;
        OnPropertyChanged(nameof(Picked));
        OnPropertyChanged(nameof(PickedAround));
        OnPropertyChanged(nameof(CanOpenPicked));
        OnPropertyChanged(nameof(CanHidePicked));
    }

    /// <summary>Whether any client on the map sits where it was dragged to, for the link that puts them all back.</summary>
    [ObservableProperty] public partial bool HasMoved { get; set; }

    /// <summary>A client dragged to a new place on the map: it is kept there, here and in every copy of the page, until the layout is reset.</summary>
    public void Move(string clientId, double x, double y)
    {
        var places = services.Settings.StudioPlaces.ToDictionary(p => p.Key, p => p.Value);
        places[clientId] = new MapPlace(Math.Clamp(x, -1, 1), Math.Clamp(y, -1, 1));
        services.Settings.StudioPlaces = places;
        Refresh();
    }

    /// <summary>Puts every client on the map that is showing (yours, or the demo studio's) back where the layout has it.</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        var here = Stars.Where(p => p.Star.ParentId is null).Select(p => p.Star.Id).ToHashSet();
        services.Settings.StudioPlaces = services.Settings.StudioPlaces.Where(p => !here.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        Refresh();
    }

    /// <summary>How many clients are left off, for the link that brings them back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHidden), nameof(HiddenText))]
    public partial int HiddenCount { get; set; }

    public bool HasHidden => HiddenCount > 0 && !Presenting;
    public string HiddenText => HiddenCount == 1 ? "1 left off. Show it again" : $"{HiddenCount} left off. Show them again";

    /// <summary>Only a real client can be left off, and not from the copy shown full screen.</summary>
    public bool CanHidePicked => !Demo && !Presenting && Picked is { Kind: StarKind.Client } star && star.Id != StudioMap.OwnId;

    /// <summary>Leaves the picked client off Studio: for a contact that isn't a client, such as a payment provider.</summary>
    [RelayCommand]
    private void HidePicked()
    {
        if (!CanHidePicked || Picked is not { } star)
            return;
        services.Settings.StudioHidden = [.. services.Settings.StudioHidden.Where(id => id != star.Id), star.Id];
        SelectedId = null;
        Refresh();
    }

    [RelayCommand]
    private void ShowHidden()
    {
        services.Settings.StudioHidden = [];
        Refresh();
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

    /// <summary>A click on a card on the wall opens its client.</summary>
    public void Open(WallCard card) => Open(card.Client);

    /// <summary>A click on a line opens its project; not in the demo studio, where there is none behind it.</summary>
    public void Open(ProjectLine line)
    {
        if (!Demo && !Presenting)
            services.Go.Project(line.ProjectId);
    }

    public void Dispose() => services.Projects.Changed -= WorkChanged;
}

/// <summary>The pictures Studio can show.</summary>
public enum StudioShow { Map, Wall, Lines }
