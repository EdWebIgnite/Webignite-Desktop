using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;
using Webignite.Tools;

namespace Webignite.Desktop.ViewModels;

// Logo pack and favicons: one master logo in; every version a client is asked for out as a zip, and the full
// favicon set for a website with the lines to paste.
public sealed partial class ToolsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    [NotifyCanExecuteChangedFor(nameof(SaveLogoPackToCommand), nameof(SaveFaviconsToCommand))]
    public partial string LogoPath { get; set; } = "";

    [ObservableProperty] public partial string LogoName { get; set; } = "";
    [ObservableProperty] public partial string LogoInfo { get; set; } = "";
    [ObservableProperty] public partial string LogoWarning { get; set; } = "";
    [ObservableProperty] public partial string LogoMessage { get; set; } = "";
    [ObservableProperty] public partial bool LogoProblem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLogoPackToCommand), nameof(SaveFaviconsToCommand))]
    public partial bool LogoBusy { get; set; }

    /// <summary>The colour behind the phone icons, which can't be see-through.</summary>
    [ObservableProperty] public partial string FaviconFill { get; set; } = "#FFFFFF";

    /// <summary>The colour phones tint the browser's bar with.</summary>
    [ObservableProperty] public partial string FaviconTheme { get; set; } = "#FFFFFF";

    /// <summary>The lines for the page's head, once a favicon set has been made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFaviconHtml))]
    public partial string FaviconHtml { get; set; } = "";

    public bool HasLogo => LogoPath.Length > 0;
    public bool HasFaviconHtml => FaviconHtml.Length > 0;

    /// <summary>The logo in colour, all white and all black, as it will look.</summary>
    public ObservableCollection<LogoPreview> LogoPreviews { get; } = [];

    private string? logoMade;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowLogoMadeCommand))]
    public partial bool HasLogoMade { get; set; }

    /// <summary>Opens a master logo: reads what it is and draws the three versions to look at.</summary>
    public async Task OpenLogoAsync(string path)
    {
        LogoBusy = true;
        LogoSaid("Reading the logo…", problem: false);
        try
        {
            var read = await Task.Run(() =>
            {
                using var master = LogoMaster.Open(path);
                var previews = new List<(string Label, (byte[] Pixels, int Width, int Height) Picture)>
                {
                    ("In colour", LogoMaker.Preview(master, 160, LogoInk.Colour, onBlack: false)),
                };
                if (master.SeeThrough)
                {
                    previews.Add(("All white", LogoMaker.Preview(master, 160, LogoInk.White, onBlack: true)));
                    previews.Add(("All black", LogoMaker.Preview(master, 160, LogoInk.Black, onBlack: false)));
                }
                return (Vector: master.IsVector, Width: (int)master.Width, Height: (int)master.Height, master.SeeThrough, previews);
            });
            ClearLogoPreviews();
            foreach (var (label, picture) in read.previews)
                LogoPreviews.Add(new LogoPreview(label, picture.Pixels, picture.Width, picture.Height));
            LogoPath = path;
            if (LogoName.Trim().Length == 0)
                LogoName = Names(Path.GetFileNameWithoutExtension(path));
            LogoInfo = read.Vector ? "An SVG: sharp at any size." : $"A picture, {read.Width:N0} × {read.Height:N0} pixels" + (read.SeeThrough ? ", with a see-through background." : ", on a solid background.");
            LogoWarning = LogoPack.Warning(read.Width, read.Vector, read.SeeThrough) ?? "";
            FaviconHtml = "";
            LogoSaid("", problem: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogoSaid(e is IOException ? e.Message : "That file couldn't be opened.", problem: true);
        }
        finally
        {
            LogoBusy = false;
        }
    }

    // "karoo-coffee_logo-final" reads better as "Karoo coffee logo final" in the name box.
    private static string Names(string stem)
    {
        var words = stem.Replace('-', ' ').Replace('_', ' ').Trim();
        return words.Length == 0 ? "" : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private bool CanMakeLogos() => HasLogo && !LogoBusy;

    /// <summary>Makes the whole logo pack as one zip at this path.</summary>
    [RelayCommand(CanExecute = nameof(CanMakeLogos))]
    private async Task SaveLogoPackTo(string zipPath)
    {
        var (path, name) = (LogoPath, LogoName);
        await LogoJobAsync("Making the logo pack…", () =>
        {
            using var master = LogoMaster.Open(path);
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            var made = LogoMaker.WritePack(master, name, zipPath);
            return (zipPath, $"{made} logo files, with a note on which to use where, are in {Path.GetFileName(zipPath)}.");
        });
    }

    /// <summary>Makes the favicon set in this folder.</summary>
    [RelayCommand(CanExecute = nameof(CanMakeLogos))]
    private async Task SaveFaviconsTo(string folder)
    {
        if (Colours.Parse(FaviconFill) is not { } fill || Colours.Parse(FaviconTheme) is not { } theme)
        {
            LogoSaid("The two colours need to be codes like #FFFFFF.", problem: true);
            return;
        }
        var (path, name) = (LogoPath, LogoName);
        string html = "";
        await LogoJobAsync("Making the favicons…", () =>
        {
            using var master = LogoMaster.Open(path);
            html = LogoMaker.WriteFavicons(master, name, folder, fill, theme);
            return (Path.Combine(folder, "favicon.ico"), $"The favicon set is in {Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))}. {Favicons.WhereItGoes}");
        });
        if (!LogoProblem)
            FaviconHtml = html;
    }

    // Brand sheet: the logo, the palette from the Colours tool and two typefaces, on one A4 page.

    [ObservableProperty] public partial string BrandHeadingFont { get; set; } = "";
    [ObservableProperty] public partial string BrandBodyFont { get; set; } = "";
    [ObservableProperty] public partial string BrandPreparedBy { get; set; } = "";

    /// <summary>The palette as it will go on the sheet. Read again each time the window is shown.</summary>
    public ObservableCollection<ShadeRow> BrandColours { get; } = [];

    public bool HasBrandColours => BrandColours.Count > 0;

    /// <summary>Reads the palette built in Colours, and who the sheet is from.</summary>
    public void LoadBrand()
    {
        BrandColours.Clear();
        foreach (var (swatch, colour) in BrandSheet.Shown(services.Settings.Palette.Swatches))
            BrandColours.Add(new ShadeRow(colour, swatch.Name));
        OnPropertyChanged(nameof(HasBrandColours));
        if (BrandPreparedBy.Length == 0)
            BrandPreparedBy = services.Settings.Xero.TenantName ?? "";
    }

    [RelayCommand]
    private void OpenColoursForBrand() => services.Go.Tool(ToolKind.Colours);

    private BrandSheetContent BrandNow => new()
    {
        Name = LogoName, LogoPath = LogoPath, Colours = services.Settings.Palette.Swatches,
        HeadingFont = BrandHeadingFont, BodyFont = BrandBodyFont, PreparedBy = BrandPreparedBy,
    };

    /// <summary>Why the brand sheet can't be saved yet, shown as a problem; null when it can.</summary>
    public string? BrandSheetProblem()
    {
        var problem = BrandSheet.Problem(BrandNow);
        if (problem is not null)
            LogoSaid(problem, problem: true);
        return problem;
    }

    /// <summary>Makes the brand sheet as a PDF at this path.</summary>
    [RelayCommand]
    private async Task SaveBrandSheetTo(string pdfPath)
    {
        if (LogoBusy || BrandSheetProblem() is not null)
            return;
        var content = BrandNow;
        IReadOnlyList<string> missing = [];
        await LogoJobAsync("Making the brand sheet…", () =>
        {
            missing = BrandSheetMaker.Write(content, pdfPath);
            return (pdfPath, $"The brand sheet is saved as {Path.GetFileName(pdfPath)}.");
        });
        if (!LogoProblem && BrandSheet.MissingFonts(missing) is { } note)
            LogoSaid(LogoMessage + " " + note, problem: false);
    }

    private async Task LogoJobAsync(string doing, Func<(string Made, string Said)> job)
    {
        LogoBusy = true;
        LogoSaid(doing, problem: false);
        logoMade = null;
        HasLogoMade = false;
        try
        {
            var (made, said) = await Task.Run(job);
            logoMade = made;
            HasLogoMade = true;
            LogoSaid(said, problem: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogoSaid("That didn't work: " + e.Message, problem: true);
        }
        finally
        {
            LogoBusy = false;
        }
    }

    private void LogoSaid(string words, bool problem)
    {
        LogoMessage = words;
        LogoProblem = problem;
    }

    [RelayCommand(CanExecute = nameof(HasLogoMade))]
    private void ShowLogoMade()
    {
        if (logoMade is { } made && File.Exists(made))
        {
            services.Go.ShowFile(made);
            services.Go.MainToFront();
        }
    }

    /// <summary>Lets go of the pictures. Called when the window closes.</summary>
    public void ClearLogoPreviews()
    {
        foreach (var preview in LogoPreviews)
            preview.Dispose();
        LogoPreviews.Clear();
    }
}

/// <summary>One version of the logo, drawn small on its tile.</summary>
public sealed class LogoPreview : IDisposable
{
    public LogoPreview(string label, byte[] bgra, int width, int height)
    {
        Label = label;
        var picture = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = picture.Lock())
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, buffer.Address, Math.Min(bgra.Length, buffer.RowBytes * height));
        Picture = picture;
    }

    public string Label { get; }
    public Bitmap Picture { get; }

    public void Dispose() => Picture.Dispose();
}
