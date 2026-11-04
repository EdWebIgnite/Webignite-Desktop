using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

public enum FillTool
{
    Text,
    Date,
    Signature,
}

/// <summary>
/// One page shown large to fill in and sign: click to put text, today's date or the signature where it goes,
/// drag them into place. The signature is drawn once and kept for next time.
/// </summary>
public sealed partial class PdfFillViewModel : ViewModelBase
{
    /// <summary>Letters as a share of the page's height: about 11 points on an A4 page.</summary>
    public const double TextSize = 0.014;

    /// <summary>How wide the page shows while filling in, in screen units.</summary>
    public const double ShownWidth = 760;

    public double ShownHeight => ShownWidth * Aspect;

    private readonly int page;
    private readonly PdfViewModel owner;

    public PdfFillViewModel(int page, Bitmap picture, IReadOnlyList<PdfStamp> placed, byte[]? signature, PdfViewModel owner)
    {
        this.page = page;
        this.owner = owner;
        Picture = picture;
        SignaturePng = signature;
        foreach (var stamp in placed)
            Stamps.Add(PdfStampRow.From(stamp, this));
    }

    internal double PageHeight => ShownHeight;

    public Bitmap Picture { get; }
    public int PageNumber => page + 1;

    /// <summary>Height over width of the page as shown.</summary>
    public double Aspect => (double)Picture.PixelSize.Height / Picture.PixelSize.Width;

    public ObservableCollection<PdfStampRow> Stamps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText), nameof(IsDate), nameof(IsSignature), nameof(Hint))]
    public partial FillTool Tool { get; set; }

    public bool IsText => Tool == FillTool.Text;
    public bool IsDate => Tool == FillTool.Date;
    public bool IsSignature => Tool == FillTool.Signature;

    public string Hint => Tool switch
    {
        FillTool.Date => "Click where today's date goes.",
        FillTool.Signature when SignaturePng is null => "Draw your signature first. It's kept for next time.",
        FillTool.Signature => "Click where your signature goes, then drag it into place.",
        _ => "Click where the words go, then type. Drag a box to move it.",
    };

    /// <summary>The signature drawn once, as a PNG with a see-through background.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSignature), nameof(Hint))]
    public partial byte[]? SignaturePng { get; private set; }

    public bool HasSignature => SignaturePng is not null;

    /// <summary>The signature pad is open.</summary>
    [ObservableProperty] public partial bool Drawing { get; set; }

    [RelayCommand]
    private void Use(FillTool tool)
    {
        Tool = tool;
        if (tool == FillTool.Signature && SignaturePng is null)
            Drawing = true;
    }

    [RelayCommand]
    private void DrawSignature() => Drawing = true;

    /// <summary>A signature drawn on the pad: kept for this and every later PDF.</summary>
    public void KeepSignature(byte[] png)
    {
        SignaturePng = png;
        owner.KeepSignature(png);
        Drawing = false;
        Tool = FillTool.Signature;
    }

    /// <summary>A click on the page, as shares of its width and height from the top left.</summary>
    public PdfStampRow? PlaceAt(double x, double y)
    {
        PdfStampRow? row = Tool switch
        {
            FillTool.Text => new PdfStampRow(this) { X = x, Y = y - TextSize / 2, Height = TextSize, IsEditing = true },
            FillTool.Date => new PdfStampRow(this) { X = x, Y = y - TextSize / 2, Height = TextSize, Text = DateTime.Today.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) },
            FillTool.Signature when SignaturePng is { } png => Signature(png, x, y),
            _ => null,
        };
        if (row is null)
        {
            Drawing = true;
            return null;
        }
        Stamps.Add(row);
        return row;
    }

    // A signature a quarter of the page wide, centred on the click, its shape kept on the page as shown.
    private PdfStampRow Signature(byte[] png, double x, double y)
    {
        using var stream = new MemoryStream(png);
        using var picture = new Bitmap(stream);
        const double width = 0.25;
        var height = width * picture.PixelSize.Height / picture.PixelSize.Width / Aspect;
        return new PdfStampRow(this) { X = x - width / 2, Y = y - height / 2, Width = width, Height = height, Png = png };
    }

    internal void Remove(PdfStampRow row) => Stamps.Remove(row);

    /// <summary>Back to the pages, keeping what was put on this one.</summary>
    [RelayCommand]
    private void Done() => owner.FinishFilling(page, [.. Stamps.Select(s => s.ToStamp(page)).OfType<PdfStamp>()]);

    public void Free()
    {
        foreach (var stamp in Stamps)
            stamp.Free();
        Picture.Dispose();
    }
}

/// <summary>Words or the signature on the page being filled in, placed as shares of the page.</summary>
public sealed partial class PdfStampRow(PdfFillViewModel owner) : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Left))]
    public partial double X { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Top))]
    public partial double Y { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoxWidth))]
    public partial double Width { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoxHeight), nameof(FontSize))]
    public partial double Height { get; set; }

    // Where it shows on the page while filling in, in screen units.
    public double Left => X * PdfFillViewModel.ShownWidth;
    public double Top => Y * owner.PageHeight;
    public double BoxWidth => Width * PdfFillViewModel.ShownWidth;
    public double BoxHeight => Height * owner.PageHeight;
    public double FontSize => Height * owner.PageHeight;

    /// <summary>Moves it by so many screen units, keeping it on the page.</summary>
    public void MoveBy(double dx, double dy)
    {
        X = Math.Clamp(X + dx / PdfFillViewModel.ShownWidth, 0, 1 - Math.Max(Width, 0.02));
        Y = Math.Clamp(Y + dy / owner.PageHeight, 0, 1 - Height);
    }
    [ObservableProperty] public partial string Text { get; set; } = "";

    /// <summary>The words' box was just put down, so the cursor goes in it.</summary>
    [ObservableProperty] public partial bool IsEditing { get; set; }

    public byte[]? Png { get; init; }
    public bool IsSignature => Png is not null;
    public bool IsWords => Png is null;

    private Bitmap? shown;

    public Bitmap? Shown => Png is null ? null : shown ??= new Bitmap(new MemoryStream(Png));

    [RelayCommand]
    private void Remove() => owner.Remove(this);

    public static PdfStampRow From(PdfStamp stamp, PdfFillViewModel owner) => new(owner)
    {
        X = stamp.X,
        Y = stamp.Y,
        Width = stamp.Width,
        Height = stamp.Height,
        Text = stamp.Text ?? "",
        Png = stamp.Picture is null ? null : PngOf(stamp),
    };

    // A placed signature keeps its PNG alongside its pixels, so it shows again when the page is opened again.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], byte[]> Pngs = [];

    private static byte[]? PngOf(PdfStamp stamp) => Pngs.TryGetValue(stamp.Picture!, out var png) ? png : null;

    public PdfStamp? ToStamp(int page)
    {
        if (Png is { } png)
        {
            var (pixels, width, height) = Bgra(png);
            Pngs.AddOrUpdate(pixels, png);
            return new PdfStamp(page, X, Y, Width, Height) { Picture = pixels, PictureWidth = width, PictureHeight = height };
        }
        return Text.Trim().Length == 0 ? null : new PdfStamp(page, X, Y, Width, Height) { Text = Text.TrimEnd() };
    }

    private static (byte[] Pixels, int Width, int Height) Bgra(byte[] png) => Webignite.Tools.PictureTools.Bgra(png);

    public void Free()
    {
        shown?.Dispose();
        shown = null;
    }
}
