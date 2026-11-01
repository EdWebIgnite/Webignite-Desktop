using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Website images: resize, compress and convert a batch, saved to a "web" folder beside the originals.
public sealed partial class ToolsViewModel
{
    public IReadOnlyList<WidthChoice> ImageWidths { get; } =
        [.. ImageRules.WidthChoices.Select(w => new WidthChoice(w, $"{w} px wide" + (w == 1920 ? " (full-width banners)" : w == 1200 ? " (blog and content)" : w == 400 ? " (thumbnails)" : ""))),
         new WidthChoice(0, "Keep the size")];

    public IReadOnlyList<FormatChoice> ImageFormats { get; } =
    [
        new(ImageFormat.WebP, "WebP (smallest, for websites)"), new(ImageFormat.Jpeg, "JPEG"),
        new(ImageFormat.Png, "PNG (logos, sharp edges)"), new(ImageFormat.Same, "Same as the original"),
    ];

    [ObservableProperty] public partial WidthChoice? ImageWidth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageHasQuality))]
    public partial FormatChoice? ImageFormatChoice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageQualityText))]
    public partial double ImageQuality { get; set; } = 80;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenImageFolderCommand))]
    public partial string? ImageFolder { get; set; }

    [ObservableProperty] public partial bool ImagesBusy { get; set; }
    [ObservableProperty] public partial string ImageMessage { get; set; } = "";

    public bool ImageHasQuality => ImageFormatChoice?.Format != ImageFormat.Png;
    public string ImageQualityText => $"Quality {(int)ImageQuality}";
    public bool HasImageResults => ImageResults.Count > 0;

    public ObservableCollection<ImageResultRow> ImageResults { get; } = [];

    private ImageJob ImageChoices => new(ImageWidth?.Width ?? 1920, 0, ImageFormatChoice?.Format ?? ImageFormat.WebP, (int)ImageQuality);

    /// <summary>Shrinks the images one at a time off the screen's thread, adding each result as it's done.</summary>
    public async Task ShrinkImagesAsync(IEnumerable<string> paths)
    {
        var files = paths.Where(ImageRules.CanRead).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            ImageMessage = "Those aren't images. Use JPG, PNG, WebP, GIF or BMP files.";
            return;
        }
        ImagesBusy = true;
        ImageResults.Clear();
        var job = ImageChoices;
        long before = 0, after = 0;
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                ImageMessage = $"Working on {i + 1} of {files.Count}...";
                var result = await Task.Run(() => ImageShrinker.Shrink(files[i], job));
                ImageResults.Add(new ImageResultRow(result));
                if (result.Problem is null)
                {
                    before += result.Before;
                    after += result.After;
                    ImageFolder = Path.GetDirectoryName(result.Output);
                }
            }
            var done = ImageResults.Count(r => !r.Problem);
            ImageMessage = done == 0 ? "None of them could be done."
                : (done == 1 ? "1 image" : $"{done} images") + $" saved in the web folder: {ImageRules.Saving(before, after)}.";
        }
        finally
        {
            ImagesBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasImageFolder))]
    private void OpenImageFolder()
    {
        if (ImageFolder is { } folder && Directory.Exists(folder))
            AppServices.OpenFile(folder);
    }

    private bool HasImageFolder() => ImageFolder is not null;
}

public sealed record WidthChoice(int Width, string Label)
{
    public override string ToString() => Label;
}

public sealed record FormatChoice(ImageFormat Format, string Label)
{
    public override string ToString() => Label;
}

public sealed record ImageResultRow(ImageShrinker.Result Result)
{
    public string Name => Path.GetFileName(Result.Source);
    public bool Problem => Result.Problem is not null;
    public string Detail => Result.Problem ?? $"{Result.Width} × {Result.Height}, {ImageRules.Saving(Result.Before, Result.After)}";
}
