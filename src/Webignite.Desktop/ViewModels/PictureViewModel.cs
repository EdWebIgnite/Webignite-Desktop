using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The picture viewer: one picture at a time, fitted to the window or zoomed, with the others in its folder a
/// key away. Only the picture on screen is held, shrunk to at most <see cref="PictureViewing.LargestSide"/>.
/// </summary>
public sealed partial class PictureViewModel(AppServices services, Action close) : ViewModelBase, IDisposable
{
    private IReadOnlyList<string> pictures = [];
    private int loads; // counts up per load, so a slow one finishing late is dropped
    private (double Width, double Height) space;

    [ObservableProperty] public partial Bitmap? Picture { get; private set; }
    [ObservableProperty] public partial string Path { get; private set; } = "";
    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string Info { get; private set; } = "";
    [ObservableProperty] public partial string Problem { get; private set; } = "";
    [ObservableProperty] public partial bool Loading { get; private set; }
    [ObservableProperty] public partial bool ConfirmingDelete { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownWidth), nameof(ShownHeight), nameof(ZoomText))]
    public partial double Zoom { get; private set; } = 1;

    /// <summary>Whether the picture follows the window's size; zooming by hand stops it until Fit is pressed.</summary>
    [ObservableProperty] public partial bool Fitting { get; private set; } = true;

    public int FullWidth { get; private set; }
    public int FullHeight { get; private set; }
    public double ShownWidth => FullWidth * Zoom;
    public double ShownHeight => FullHeight * Zoom;
    public string ZoomText => PictureViewing.Percent(Zoom);
    public bool HasProblem => Problem.Length > 0;
    public bool HasMore => pictures.Count > 1;

    /// <summary>Set by the window: puts the picture on the clipboard.</summary>
    public Func<Bitmap, Task>? CopyPicture { get; set; }

    /// <summary>Shows a picture, with the others in its folder ready for the arrow keys.</summary>
    public void Show(string path)
    {
        var folder = System.IO.Path.GetDirectoryName(path);
        pictures = folder is not null && Directory.Exists(folder)
            ? PictureViewing.Pictures(Directory.EnumerateFiles(folder))
            : [path];
        if (!pictures.Contains(path, StringComparer.OrdinalIgnoreCase))
            pictures = [path, .. pictures];
        OnPropertyChanged(nameof(HasMore));
        Load(path);
    }

    /// <summary>The space the picture has, from the window, so Fit knows how big to make it.</summary>
    public void SpaceIs(double width, double height)
    {
        space = (width, height);
        if (Fitting)
            Zoom = PictureViewing.Fit(FullWidth, FullHeight, width, height);
    }

    private int Index => pictures.ToList().FindIndex(p => string.Equals(p, Path, StringComparison.OrdinalIgnoreCase));

    private void Load(string path)
    {
        var load = ++loads;
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Info = "";
        Problem = "";
        OnPropertyChanged(nameof(HasProblem));
        ConfirmingDelete = false;
        KeepResult();
        Tool = PictureTool.None;
        CropArea = null;
        Loading = true;
        LoadAsync(path, load).Forget("Showing a picture");
    }

    private async Task LoadAsync(string path, int load)
    {
        try
        {
            var (pixels, width, height, fullWidth, fullHeight) =
                await Task.Run(() => Webignite.Tools.PictureTools.ForViewing(path, PictureViewing.LargestSide));
            if (load != loads)
                return;
            var old = Picture;
            Picture = ToBitmap(pixels, width, height);
            old?.Dispose();
            FullWidth = fullWidth;
            FullHeight = fullHeight;
            Fitting = true;
            Zoom = PictureViewing.Fit(fullWidth, fullHeight, space.Width, space.Height);
            OnPropertyChanged(nameof(ShownWidth));
            OnPropertyChanged(nameof(ShownHeight));
            Info = PictureViewing.Describe(Math.Max(0, Index), pictures.Count, fullWidth, fullHeight, Size(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (load != loads)
                return;
            var old = Picture;
            Picture = null;
            old?.Dispose();
            Problem = $"Couldn't show {System.IO.Path.GetFileName(path)}. {e.Message}";
            OnPropertyChanged(nameof(HasProblem));
        }
        finally
        {
            if (load == loads)
                Loading = false;
        }
    }

    internal static WriteableBitmap ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var row = 0; row < height; row++)
            System.Runtime.InteropServices.Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
        return bitmap;
    }

    private static long Size(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    [RelayCommand]
    private void Next() => Step(+1);

    [RelayCommand]
    private void Previous() => Step(-1);

    private void Step(int by)
    {
        if (pictures.Count < 2)
            return;
        Load(pictures[PictureViewing.Step(Math.Max(0, Index), by, pictures.Count)]);
    }

    [RelayCommand]
    private void ZoomIn() => ZoomTo(PictureViewing.In(Zoom));

    [RelayCommand]
    private void ZoomOut() => ZoomTo(PictureViewing.Out(Zoom));

    [RelayCommand]
    private void ActualSize() => ZoomTo(1);

    [RelayCommand]
    private void Fit()
    {
        Fitting = true;
        Zoom = PictureViewing.Fit(FullWidth, FullHeight, space.Width, space.Height);
    }

    public void ZoomTo(double zoom)
    {
        Fitting = false;
        Zoom = Math.Clamp(zoom, PictureViewing.MinZoom, PictureViewing.MaxZoom);
    }

    [RelayCommand]
    private Task TurnRight() => Turn(90);

    [RelayCommand]
    private Task TurnLeft() => Turn(-90);

    // Saved in the file, like Files' Turn; Undo puts the file back as it was.
    private Task Turn(int degrees) => EditInPlace("Turned", path => Webignite.Tools.PictureTools.Rotate(path, degrees));

    [RelayCommand]
    private async Task Copy()
    {
        if (Picture is { } picture && CopyPicture is { } copy)
        {
            await copy(picture);
            services.ShowNote?.Invoke("Picture copied", $"{Name} is on the clipboard, ready to paste.", null);
        }
    }

    [RelayCommand]
    private void Markup()
    {
        if (Path.Length > 0)
            services.Go.Markup(Path);
    }

    [RelayCommand]
    private void ShowInFiles()
    {
        if (Path.Length > 0)
            services.Go.ShowFile(Path);
    }

    [RelayCommand]
    private void OpenWith()
    {
        try
        {
            FileOps.Open(Path);
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Problem = "Couldn't open it in another program. " + e.Message;
            OnPropertyChanged(nameof(HasProblem));
        }
    }

    [RelayCommand]
    private void AskDelete() => ConfirmingDelete = Path.Length > 0;

    [RelayCommand]
    private void KeepIt() => ConfirmingDelete = false;

    /// <summary>To the Recycle Bin, then on to the next picture, or the viewer closes when it was the last.</summary>
    [RelayCommand]
    private void Delete()
    {
        var gone = Path;
        var index = Math.Max(0, Index);
        try
        {
            FileOps.Recycle([gone]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            ConfirmingDelete = false;
            Problem = "Couldn't delete it. " + e.Message;
            OnPropertyChanged(nameof(HasProblem));
            return;
        }
        services.ShowNote?.Invoke("Moved to the Recycle Bin", $"{System.IO.Path.GetFileName(gone)} can be got back from there.", null);
        pictures = [.. pictures.Where(p => !string.Equals(p, gone, StringComparison.OrdinalIgnoreCase))];
        OnPropertyChanged(nameof(HasMore));
        if (pictures.Count == 0)
        {
            close();
            return;
        }
        Load(pictures[Math.Min(index, pictures.Count - 1)]);
    }

    public void Dispose()
    {
        loads++;
        Picture?.Dispose();
        Picture = null;
        AfterPicture?.Dispose();
        AfterPicture = null;
        CopyPicture = null;
        CopyText = null;
    }
}
