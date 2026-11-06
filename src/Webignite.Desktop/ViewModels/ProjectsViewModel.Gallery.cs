using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The template gallery, as Asana's: the templates that come with the app filed by category, the person's own under
// My templates, and a blank project. A template opens to show what's in it, with a name and a start date to use it.
public sealed partial class ProjectsViewModel
{
    public const string AllTemplates = "All";
    public const string MyTemplates = "My templates";

    /// <summary>The chips along the top: all, the person's own, then each category that has templates.</summary>
    public IReadOnlyList<GalleryChip> GalleryChips => galleryChips ??= [.. new[] { AllTemplates, MyTemplates }.Concat(BuiltInTemplates.Categories).Select(c => new GalleryChip(c, this))];

    private IReadOnlyList<GalleryChip>? galleryChips;

    [ObservableProperty] public partial string GalleryCategory { get; private set; } = AllTemplates;

    partial void OnGalleryCategoryChanged(string value)
    {
        foreach (var chip in GalleryChips)
            chip.IsOn = chip.Name == value;
    }

    public ObservableCollection<GalleryCard> GalleryCards { get; } = [];

    public bool GalleryEmpty => IsTemplates && Previewing is null && GalleryCards.Count == 0;

    /// <summary>The template opened to look at, or null for the gallery.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewing), nameof(IsGallery), nameof(GalleryEmpty))]
    public partial GalleryCard? Previewing { get; private set; }

    public bool IsPreviewing => IsTemplates && Previewing is not null;
    public bool IsGallery => IsTemplates && Previewing is null;

    /// <summary>The opened template's headings, each with its tasks, as the project will start.</summary>
    public IReadOnlyList<GalleryHeading> PreviewHeadings { get; private set; } = [];

    [ObservableProperty] public partial string PreviewName { get; set; } = "";
    [ObservableProperty] public partial DateTime? PreviewStart { get; set; }

    public void ShowCategory(string category)
    {
        GalleryCategory = category;
        Previewing = null;
        ListTemplates();
    }

    private void ListTemplates()
    {
        GalleryCards.Clear();
        var mine = Templates.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).Select(t => GalleryCard.Mine(t, this));
        var builtIn = BuiltInTemplates.All.Select(t => GalleryCard.Of(t, this));
        var shown = GalleryCategory switch
        {
            AllTemplates => mine.Concat(builtIn),
            MyTemplates => mine,
            _ => builtIn.Where(c => c.Category == GalleryCategory),
        };
        foreach (var card in shown)
            GalleryCards.Add(card);
        OnPropertyChanged(nameof(GalleryEmpty));
        OnPropertyChanged(nameof(IsGallery));
        OnPropertyChanged(nameof(IsPreviewing));
    }

    /// <summary>Opens a template to see its headings and tasks, with the name and start date to use it with.</summary>
    public void Preview(GalleryCard card)
    {
        Previewing = card;
        PreviewName = NewProjectName.Trim().Length > 0 ? NewProjectName.Trim() : card.Name;
        PreviewStart = DateTime.Today;
        PreviewHeadings = [.. card.Template.Headings.Select(h => new GalleryHeading(h,
            [.. card.Template.Tasks.Where(t => t.Heading == h).Select(t => t.Subtasks.Count > 0 ? $"{t.Title} ({t.Subtasks.Count} subtasks)" : t.Title)]))];
        OnPropertyChanged(nameof(PreviewHeadings));
    }

    [RelayCommand]
    private void BackToGallery() => Previewing = null;

    /// <summary>Starts a project from the opened template, named as typed, its dates counted from the start date.</summary>
    [RelayCommand]
    private void UseTemplate()
    {
        if (Previewing is not { } card)
            return;
        SaveTyped();
        var start = DateOnly.FromDateTime(PreviewStart ?? DateTime.Today);
        var saved = Ours(() => services.Projects.AddFromTemplate(card.Template, PreviewName, start));
        // A board template opens on its board.
        if (card.Board)
            StartLayout(saved.Id, new ProjectLayout { Board = true });
        // A template from the gallery brings its own colour and icon, as Asana's do.
        if (card.Icon is { } icon && card.Colour is { } colour)
            saved = Ours(() => services.Projects.Change(saved.Id, p => p with { Icon = icon, Colour = colour })).Saved ?? saved;
        Previewing = null;
        NewProjectName = "";
        ListMessage = "";
        ShowList();
        Show(saved);
    }

    [RelayCommand]
    private void RemovePreviewed()
    {
        if (Previewing is { IsMine: true } card)
        {
            Previewing = null;
            RemoveTemplate(card.Template);
        }
    }

    /// <summary>A blank project, from the gallery's button, as Asana's.</summary>
    [RelayCommand]
    private void BlankProject() => NewProjectCommand.Execute(null);
}

/// <summary>A template in the gallery: its square, name and what it's for.</summary>
public sealed partial class GalleryCard(ProjectTemplate template, string category, string description, string? icon, string? colour, bool board, ProjectsViewModel page)
{
    public ProjectTemplate Template { get; } = template;
    public string Name => Template.Name;
    public string Category { get; } = category;
    public string Description { get; } = description;
    public string? Icon { get; } = icon;
    public string? Colour { get; } = colour;
    public bool Board { get; } = board;
    public bool IsMine => Category == ProjectsViewModel.MyTemplates;

    /// <summary>The square's look: the template's own, or a grey board for the person's own templates.</summary>
    public string TileIcon => Icon ?? "Board";
    public string TileColour => Colour ?? "CoolGrey";

    public string About => $"{Count(Template.Headings.Count, "heading")}, {Count(Template.Tasks.Count, "task")}";

    private static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    public static GalleryCard Of(BuiltInTemplate t, ProjectsViewModel page) => new(t.Template, t.Category, t.Description, t.Icon, t.Colour, t.Board, page);

    public static GalleryCard Mine(ProjectTemplate t, ProjectsViewModel page) =>
        new(t, ProjectsViewModel.MyTemplates, "Saved from one of your projects.", null, null, false, page);

    [RelayCommand]
    private void Open() => page.Preview(this);
}

/// <summary>A category along the top of the gallery; the one picked is a pill.</summary>
public sealed partial class GalleryChip : ObservableObject
{
    private readonly ProjectsViewModel page;

    public GalleryChip(string name, ProjectsViewModel page)
    {
        this.page = page;
        Name = name;
        IsOn = name == page.GalleryCategory;
    }

    public string Name { get; }

    [ObservableProperty] public partial bool IsOn { get; set; }

    [RelayCommand]
    private void Show() => page.ShowCategory(Name);
}

/// <summary>A heading in a template's preview, with its tasks.</summary>
public sealed record GalleryHeading(string Name, IReadOnlyList<string> Tasks);
