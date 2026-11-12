using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;
using Webignite.Tools;

namespace Webignite.Desktop.ViewModels;

// Social images: one picture cut to the shapes Facebook, Instagram, LinkedIn, WhatsApp and link previews want,
// shown before saving, then saved in one go.
public sealed partial class SocialToolViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices services;

    public SocialToolViewModel(AppServices services)
    {
        this.services = services;
    }

    /// <summary>The tool's window closed: what it started is stopped and what it held is freed.</summary>
    public void Dispose()
    {
        ClearSocialPreviews();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialPicture))]
    [NotifyCanExecuteChangedFor(nameof(SaveSocialToCommand))]
    public partial string SocialPath { get; set; } = "";

    [ObservableProperty] public partial string SocialName { get; set; } = "";
    [ObservableProperty] public partial string SocialInfo { get; set; } = "";
    [ObservableProperty] public partial string SocialMessage { get; set; } = "";
    [ObservableProperty] public partial bool SocialProblem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSocialToCommand))]
    public partial bool SocialBusy { get; set; }

    public bool HasSocialPicture => SocialPath.Length > 0;

    public IReadOnlyList<SocialChoice> SocialChoices => socialChoices ??= [.. SocialSizes.All.Select(s => new SocialChoice(s, this) { IsOn = s.Usual })];

    private IReadOnlyList<SocialChoice>? socialChoices;

    public IReadOnlyList<FocusChoice> SocialFocuses { get; } =
    [
        new(SocialFocus.Centre, "The middle"), new(SocialFocus.Top, "The top"), new(SocialFocus.Bottom, "The bottom"),
        new(SocialFocus.Left, "The left"), new(SocialFocus.Right, "The right"),
    ];

    [ObservableProperty] public partial FocusChoice? SocialFocusChoice { get; set; }

    public IReadOnlyList<string> SocialFormats { get; } = ["JPG", "PNG"];

    [ObservableProperty] public partial string SocialFormat { get; set; } = "JPG";

    /// <summary>Each ticked size as it will be cut.</summary>
    public ObservableCollection<SocialPreview> SocialPreviews { get; } = [];

    private string? socialMade;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowSocialMadeCommand))]
    public partial bool HasSocialMade { get; set; }

    private IReadOnlyList<SocialSize> SocialWanted => [.. SocialChoices.Where(c => c.IsOn).Select(c => c.Size)];

    private SocialFocus SocialFocusNow => SocialFocusChoice?.Focus ?? SocialFocus.Centre;

    /// <summary>Opens the picture to cut and shows each ticked size.</summary>
    public async Task OpenSocialAsync(string path)
    {
        SocialFocusChoice ??= SocialFocuses[0];
        SocialBusy = true;
        SocialSaid("Reading the picture…", problem: false);
        try
        {
            var (width, height) = await Task.Run(() => SocialImages.Size(path));
            SocialPath = path;
            SocialName = Path.GetFileNameWithoutExtension(path);
            SocialInfo = $"{width:N0} × {height:N0} pixels.";
            SocialSaid("", problem: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SocialSaid(e is IOException ? e.Message : "That file couldn't be opened.", problem: true);
            return;
        }
        finally
        {
            SocialBusy = false;
        }
        await ShowSocialPreviewsAsync();
    }

    partial void OnSocialFocusChoiceChanged(FocusChoice? value) => SocialChoicesChanged();

    /// <summary>A size was ticked or unticked, or the part to keep changed: the previews are drawn again.</summary>
    internal void SocialChoicesChanged()
    {
        SaveSocialToCommand.NotifyCanExecuteChanged();
        if (HasSocialPicture)
            ShowSocialPreviewsAsync().Forget("Showing the social pictures");
    }

    private int socialRound;

    private async Task ShowSocialPreviewsAsync()
    {
        var round = ++socialRound;
        var (path, wanted, focus) = (SocialPath, SocialWanted, SocialFocusNow);
        try
        {
            var shown = await Task.Run(() => SocialImages.Previews(path, wanted, focus, 150));
            // A newer choice was made while these were being drawn.
            if (round != socialRound)
                return;
            ClearSocialPreviews();
            foreach (var (size, pixels, width, height, soft) in shown)
                SocialPreviews.Add(new SocialPreview(size, new LogoPreview(size.Label, pixels, width, height), soft));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SocialSaid("The picture couldn't be read: " + e.Message, problem: true);
        }
    }

    private bool CanSaveSocial() => HasSocialPicture && !SocialBusy && SocialChoices.Any(c => c.IsOn);

    /// <summary>Saves every ticked size into this folder.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveSocial))]
    private async Task SaveSocialTo(string folder)
    {
        var (path, name, wanted, focus, format) = (SocialPath, SocialName, SocialWanted, SocialFocusNow, SocialFormat == "PNG" ? "png" : "jpg");
        SocialBusy = true;
        SocialSaid("Saving the pictures…", problem: false);
        socialMade = null;
        HasSocialMade = false;
        try
        {
            var (made, soft, last) = await Task.Run(() => SocialImages.Save(path, name, wanted, focus, format, folder));
            socialMade = last;
            HasSocialMade = made > 0;
            SocialSaid(SocialSizes.Saved(made, soft), problem: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SocialSaid("That didn't work: " + e.Message, problem: true);
        }
        finally
        {
            SocialBusy = false;
        }
    }

    private void SocialSaid(string words, bool problem)
    {
        SocialMessage = words;
        SocialProblem = problem;
    }

    [RelayCommand(CanExecute = nameof(HasSocialMade))]
    private void ShowSocialMade()
    {
        if (socialMade is { } made && File.Exists(made))
        {
            services.Go.ShowFile(made);
            services.Go.MainToFront();
        }
    }

    /// <summary>Lets go of the small pictures. Called when the window closes.</summary>
    public void ClearSocialPreviews()
    {
        foreach (var preview in SocialPreviews)
            preview.Dispose();
        SocialPreviews.Clear();
    }
}

public sealed partial class SocialChoice(SocialSize size, SocialToolViewModel tools) : ObservableObject
{
    public SocialSize Size { get; } = size;
    public string Label => $"{Size.Label} ({Size.Pixels})";

    [ObservableProperty] public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value) => tools.SocialChoicesChanged();
}

public sealed record FocusChoice(SocialFocus Focus, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One size as it will be cut, small, with whether it had to be enlarged.</summary>
public sealed class SocialPreview(SocialSize size, LogoPreview picture, bool soft) : IDisposable
{
    public string Label => size.Label;
    public string Pixels => size.Pixels + (soft ? ", enlarged" : "");
    public bool Soft => soft;
    public Avalonia.Media.Imaging.Bitmap Picture => picture.Picture;

    public void Dispose() => picture.Dispose();
}
