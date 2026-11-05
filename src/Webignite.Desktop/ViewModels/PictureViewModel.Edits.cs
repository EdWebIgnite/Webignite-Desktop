using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Core.Tools;
using Webignite.Tools;

namespace Webignite.Desktop.ViewModels;

/// <summary>What the picture viewer is doing besides showing: one tool at a time.</summary>
public enum PictureTool { None, Crop, Resize, Pick, Result }

// The viewer's tools. Crop, resize, turn and flip change the file, with Undo putting the old one back; convert and
// shrink make a new file beside it and show before and after.
public sealed partial class PictureViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cropping), nameof(Resizing), nameof(Picking), nameof(ShowingResult), nameof(UsingTool))]
    public partial PictureTool Tool { get; private set; }

    public bool Cropping => Tool == PictureTool.Crop;
    public bool Resizing => Tool == PictureTool.Resize;
    public bool Picking => Tool == PictureTool.Pick;
    public bool ShowingResult => Tool == PictureTool.Result;
    public bool UsingTool => Tool != PictureTool.None;
    [ObservableProperty] public partial bool Busy { get; private set; }

    /// <summary>Set by the window: puts text (a colour's code) on the clipboard.</summary>
    public Func<string, Task>? CopyText { get; set; }

    private void Use(PictureTool tool)
    {
        Tool = Picture is null && tool != PictureTool.None ? PictureTool.None : tool;
        ToolProblem = "";
    }

    [RelayCommand]
    private void StopTool()
    {
        if (Tool == PictureTool.Result)
            KeepResult();
        Tool = PictureTool.None;
        CropArea = null;
        ToolProblem = "";
    }

    [ObservableProperty] public partial string ToolProblem { get; private set; } = "";

    // Crop: drag over the part to keep, on the picture as shown.

    /// <summary>The part dragged over, in the picture's own pixels, or null before a drag.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CropText), nameof(CropShown))]
    public partial ScreenGrab.Area? CropArea { get; private set; }

    public string CropText => CropArea is { } a ? $"{a.Width} × {a.Height}" : "Drag over the part to keep.";

    /// <summary>The crop as drawn over the picture on screen.</summary>
    public Rect CropShown => CropArea is { } a ? new Rect(a.X * Zoom, a.Y * Zoom, a.Width * Zoom, a.Height * Zoom) : default;

    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(CropShown));

    [RelayCommand]
    private void StartCrop()
    {
        Use(PictureTool.Crop);
        CropArea = null;
    }

    /// <summary>From the window, as the mouse is dragged over the picture as shown.</summary>
    public void DragCrop(Point from, Point to) =>
        CropArea = PictureEditing.Crop(from.X, from.Y, to.X, to.Y, Zoom, FullWidth, FullHeight);

    [RelayCommand]
    private Task ApplyCrop()
    {
        if (CropArea is not { } area || !PictureEditing.IsCrop(area, FullWidth, FullHeight))
        {
            ToolProblem = "Drag over the part of the picture to keep, then Crop.";
            return Task.CompletedTask;
        }
        return EditInPlace("Cropped", path => PictureTools.Crop(path, area.X, area.Y, area.Width, area.Height));
    }

    // Resize: a new width or height, keeping the shape unless told otherwise.

    [ObservableProperty] public partial string ResizeWidth { get; set; } = "";
    [ObservableProperty] public partial string ResizeHeight { get; set; } = "";
    [ObservableProperty] public partial bool KeepShape { get; set; } = true;
    private bool filling;

    [RelayCommand]
    private void StartResize()
    {
        Use(PictureTool.Resize);
        filling = true;
        ResizeWidth = FullWidth.ToString(CultureInfo.InvariantCulture);
        ResizeHeight = FullHeight.ToString(CultureInfo.InvariantCulture);
        filling = false;
    }

    partial void OnResizeWidthChanged(string value) => Follow(value, w => ResizeHeight = PictureEditing.OtherSide(w, FullWidth, FullHeight).ToString(CultureInfo.InvariantCulture));

    partial void OnResizeHeightChanged(string value) => Follow(value, h => ResizeWidth = PictureEditing.OtherSide(h, FullHeight, FullWidth).ToString(CultureInfo.InvariantCulture));

    // With the shape kept, typing one side fills in the other.
    private void Follow(string typed, Action<int> other)
    {
        if (filling || !KeepShape || !int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var side))
            return;
        filling = true;
        other(side);
        filling = false;
    }

    [RelayCommand]
    private void ResizeTo(string percent)
    {
        var share = int.Parse(percent, CultureInfo.InvariantCulture) / 100.0;
        filling = true;
        ResizeWidth = Math.Max(1, (int)Math.Round(FullWidth * share)).ToString(CultureInfo.InvariantCulture);
        ResizeHeight = Math.Max(1, (int)Math.Round(FullHeight * share)).ToString(CultureInfo.InvariantCulture);
        filling = false;
    }

    [RelayCommand]
    private Task ApplyResize()
    {
        int? Read(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
        var (width, height, problem) = PictureEditing.Resize(FullWidth, FullHeight, Read(ResizeWidth), Read(ResizeHeight), KeepShape);
        if (problem is not null)
        {
            ToolProblem = problem;
            return Task.CompletedTask;
        }
        return EditInPlace("Resized", path => PictureTools.Resize(path, width, height));
    }

    [RelayCommand]
    private Task FlipAcross() => EditInPlace("Flipped", path => PictureTools.Flip(path, topToBottom: false));

    [RelayCommand]
    private Task FlipUpDown() => EditInPlace("Flipped", path => PictureTools.Flip(path, topToBottom: true));

    /// <summary>
    /// Changes the file: a copy of how it was is kept first, so Undo can put it back exactly, without the loss of
    /// saving a photo twice.
    /// </summary>
    private async Task EditInPlace(string what, Action<string> edit)
    {
        var path = Path;
        if (path.Length == 0 || Picture is null || Busy)
            return;
        Busy = true;
        string backup;
        try
        {
            backup = await Task.Run(() =>
            {
                var folder = Directory.CreateTempSubdirectory("webignite-undo-").FullName;
                var copy = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(path));
                File.Copy(path, copy);
                edit(path);
                return copy;
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ToolProblem = $"Couldn't do that. {e.Message}";
            Busy = false;
            return;
        }
        Busy = false;
        Tool = PictureTool.None;
        CropArea = null;
        if (Path == path)
            Load(path);
        services.OfferUndo($"{what} {System.IO.Path.GetFileName(path)}", () => PutBack(path, backup).Forget("Putting a picture back"));
    }

    private async Task PutBack(string path, string backup)
    {
        await Task.Run(() => File.Copy(backup, path, overwrite: true));
        if (Path == path)
            Load(path);
    }

    // Pick a colour from the picture: a click copies its code.

    [ObservableProperty] public partial string PickedHex { get; private set; } = "";

    [RelayCommand]
    private void StartPicking()
    {
        Use(PictureTool.Pick);
        PickedHex = "";
    }

    /// <summary>The colour at this spot on the picture as shown, read from the pixels on screen.</summary>
    public Rgb? ColourAt(Point shown)
    {
        if (Picture is not WriteableBitmap bitmap || Zoom <= 0 || FullWidth <= 0)
            return null;
        var scale = bitmap.PixelSize.Width / (double)FullWidth; // the picture on screen may be shrunk
        var x = Math.Clamp((int)(shown.X / Zoom * scale), 0, bitmap.PixelSize.Width - 1);
        var y = Math.Clamp((int)(shown.Y / Zoom * scale), 0, bitmap.PixelSize.Height - 1);
        using var buffer = bitmap.Lock();
        var pixel = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(buffer.Address + y * buffer.RowBytes + x * 4);
        // Premultiplied BGRA; pictures with see-through parts read as they show on black.
        return new Rgb((byte)(pixel >> 16 & 0xFF), (byte)(pixel >> 8 & 0xFF), (byte)(pixel & 0xFF));
    }

    public async Task PickAt(Point shown)
    {
        if (ColourAt(shown) is not { } colour)
            return;
        PickedHex = colour.Hex;
        if (CopyText is { } copy)
            await copy(colour.Hex);
        services.RememberColour(colour.Hex);
    }

    // Convert and shrink: a new file beside the original, shown before and after.

    [ObservableProperty] public partial Bitmap? AfterPicture { get; private set; }
    [ObservableProperty] public partial string BeforeText { get; private set; } = "";
    [ObservableProperty] public partial string AfterText { get; private set; } = "";
    [ObservableProperty] public partial string ResultText { get; private set; } = "";
    private string? resultPath;

    [RelayCommand]
    private Task ConvertTo(string format) => MakeCopy(path => ImageShrinker.Convert(path, Enum.Parse<ImageFormat>(format)));

    [RelayCommand]
    private Task ShrinkForWeb() => MakeCopy(path => ImageShrinker.Shrink(path, new ImageJob()));

    private async Task MakeCopy(Func<string, ImageShrinker.Result> make)
    {
        var path = Path;
        if (path.Length == 0 || Picture is null || Busy)
            return;
        KeepResult();
        Busy = true;
        ImageShrinker.Result result;
        (byte[] Pixels, int Width, int Height, int FullWidth, int FullHeight)? after = null;
        try
        {
            result = await Task.Run(() => make(path));
            if (result.Output is { } output)
                after = await Task.Run(() => PictureTools.ForViewing(output, 1600));
        }
        finally
        {
            Busy = false;
        }
        if (result.Problem is { } problem || result.Output is null || after is not { } shown)
        {
            ToolProblem = $"Couldn't make it. {result.Problem}";
            return;
        }
        resultPath = result.Output;
        AfterPicture = ToBitmap(shown.Pixels, shown.Width, shown.Height);
        BeforeText = $"Before: {FileSizes.Format(result.Before)}, {FullWidth} × {FullHeight}";
        AfterText = $"After: {FileSizes.Format(result.After)}, {result.Width} × {result.Height}, {System.IO.Path.GetExtension(result.Output).TrimStart('.').ToUpperInvariant()}";
        ResultText = $"{ImageRules.Saving(result.Before, result.After)}. Saved as {System.IO.Path.GetFileName(result.Output)}"
            + (System.IO.Path.GetDirectoryName(result.Output) is { } folder && !string.Equals(folder, System.IO.Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase)
                ? $" in {System.IO.Path.GetFileName(folder)}." : ".");
        Tool = PictureTool.Result;
    }

    /// <summary>Leaves the new file where it is and closes before and after.</summary>
    [RelayCommand]
    private void KeepResult()
    {
        var old = AfterPicture;
        AfterPicture = null;
        old?.Dispose();
        resultPath = null;
        if (Tool == PictureTool.Result)
            Tool = PictureTool.None;
    }

    [RelayCommand]
    private void ThrowResultAway()
    {
        if (resultPath is { } made)
        {
            try
            {
                File.Delete(made);
                // A "web" folder made just for it goes too.
                if (System.IO.Path.GetDirectoryName(made) is { } folder && System.IO.Path.GetFileName(folder) == "web" && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
            catch (IOException e)
            {
                ToolProblem = "Couldn't delete it. " + e.Message;
                return;
            }
        }
        KeepResult();
    }

    [RelayCommand]
    private void OpenResult()
    {
        if (resultPath is not { } made)
            return;
        KeepResult();
        Show(made);
    }

    [RelayCommand]
    private void ShowResultInFiles()
    {
        if (resultPath is { } made)
            services.Go.ShowFile(made);
    }
}
