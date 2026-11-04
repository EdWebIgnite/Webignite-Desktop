using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// A look at one file or folder: for the details pane beside the list (small), and Quick Look (big, Space).
/// Shows the picture, the PDF's pages or the text, and the details: kind, size, dates, picture size, PDF pages,
/// tags. What it draws is let go by <see cref="Free"/>, when the pick changes or the pane closes.
/// </summary>
public sealed partial class FileLookViewModel : ViewModelBase
{
    private readonly FileEntry entry;
    private readonly bool big;
    private bool freed;

    public FileLookViewModel(FileEntry entry, bool big, IReadOnlyList<FileTag> tags, string? snippet)
    {
        this.entry = entry;
        this.big = big;
        Tags = tags;
        Snippet = snippet;
        if (!entry.IsFolder)
            IsLoading = FileKinds.CanPreview(entry.Name) || FileSearch.CanLookInside(entry.Name);
    }

    public string Path => entry.Path;
    public string Name => entry.Name;
    public string KindText => entry.Kind;
    public string SizeText => entry.IsFolder ? "" : FileSizes.Format(entry.Size);
    public string ModifiedText => entry.Modified.ToString("f", CultureInfo.CurrentCulture);
    public string Where => System.IO.Path.GetDirectoryName(entry.Path.TrimEnd('\\', '/')) ?? "";
    public string Icon => entry.IsFolder ? "Folder" : FileKinds.IsPicture(entry.Name) ? "Picture" : "Quotes";
    public bool IsFolder => entry.IsFolder;
    public IReadOnlyList<FileTag> Tags { get; }
    public bool HasTags => Tags.Count > 0;
    public string TagText => string.Join(", ", Tags.Select(t => t.Name));

    /// <summary>The words around a search hit inside the file.</summary>
    public string? Snippet { get; }
    public bool HasSnippet => Snippet is not null;

    [ObservableProperty] public partial string CreatedText { get; private set; } = "";

    /// <summary>"1920 × 1080" for a picture, "4 pages" for a PDF, "12 items" for a folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMeasure))]
    public partial string Measure { get; private set; } = "";

    public bool HasMeasure => Measure.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText), nameof(ShowsIcon))]
    public partial string? Text { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsIcon))]
    public partial bool IsLoading { get; private set; }

    public ObservableCollection<Bitmap> Pictures { get; } = [];
    public bool HasPictures => Pictures.Count > 0;
    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool ShowsIcon => !IsLoading && !HasPictures && !HasText;

    /// <summary>Reads the details and draws the preview, off the screen's thread.</summary>
    public async Task Load()
    {
        try
        {
            var created = await Task.Run(() => entry.IsFolder ? Directory.GetCreationTime(entry.Path) : File.GetCreationTime(entry.Path));
            CreatedText = created.ToString("f", CultureInfo.CurrentCulture);
            if (entry.IsFolder)
            {
                var count = await Task.Run(() => FileOps.List(entry.Path).Count);
                Measure = count == 1 ? "1 item" : $"{count} items";
                return;
            }
            if (FileKinds.IsPicture(entry.Name))
                await LoadPicture();
            else if (FileKinds.IsPdf(entry.Name))
                await LoadPdf();
            else if (FileSearch.CanLookInside(entry.Name))
                Text = await Task.Run(() => FileReading.Text(entry.Path, big ? 200_000 : 1_500));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Whatever can't be read just isn't shown; the name and the rest still are.
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasPictures));
            OnPropertyChanged(nameof(ShowsIcon));
        }
    }

    private async Task LoadPicture()
    {
        var (bitmap, size) = await Task.Run(() =>
        {
            using var stream = File.OpenRead(entry.Path);
            return (Bitmap.DecodeToWidth(stream, big ? 1600 : 520, BitmapInterpolationMode.MediumQuality), PictureSize(entry.Path));
        });
        if (size is { } known)
            Measure = $"{known.Width} × {known.Height}";
        Keep([bitmap]);
    }

    private async Task LoadPdf()
    {
        var data = await File.ReadAllBytesAsync(entry.Path);
        var (pages, count) = await Task.Run(() => RenderPdf(data, big ? 1100 : 520, big ? Webignite.Core.Mail.AttachmentPreview.MaxPages : 1));
        Measure = count == 1 ? "1 page" : $"{count} pages";
        Keep(pages);
    }

    // Methods of their own, so the picture and PDF libraries are only read from disk when a look needs them.
    private static (int Width, int Height)? PictureSize(string path) => Webignite.Tools.PictureTools.Size(path);

    private static (IReadOnlyList<Bitmap> Pages, int Count) RenderPdf(byte[] data, int width, int most) => Webignite.Tools.PdfPages.Render(data, width, most);

    private void Keep(IReadOnlyList<Bitmap> drawn)
    {
        if (freed)
        {
            foreach (var bitmap in drawn)
                bitmap.Dispose();
            return;
        }
        foreach (var bitmap in drawn)
            Pictures.Add(bitmap);
    }

    /// <summary>Lets go of the pictures. Anything still being drawn is let go when it finishes.</summary>
    public void Free()
    {
        freed = true;
        var pictures = Pictures.ToList();
        Pictures.Clear();
        Text = null;
        OnPropertyChanged(nameof(HasPictures));
        foreach (var picture in pictures)
            Dispatcher.UIThread.Post(picture.Dispose, DispatcherPriority.Background);
    }
}

/// <summary>A summary for several picked items in the details pane.</summary>
public sealed record FilePickSummary(int Count, long? Size)
{
    public string Text => $"{Count} items picked" + (Size is { } size ? $", {FileSizes.Format(size)}" : "");
}
