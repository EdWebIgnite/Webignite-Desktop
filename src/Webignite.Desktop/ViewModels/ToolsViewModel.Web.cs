using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

/// <summary>The helpers in the Web helpers window, one shown at a time.</summary>
public enum WebHelper
{
    Lorem,
    Placeholder,
    Units,
    Minify,
    Json,
    Cases,
    Utm,
}

// Web helpers: filler text, placeholder pictures, px and rem, smaller CSS and JavaScript, tidy JSON, slugs and
// campaign links. Nothing is saved; each answer is there to copy. The rules are in Core (WebHelpers.cs).
public sealed partial class ToolsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLorem), nameof(IsPlaceholder), nameof(IsUnits), nameof(IsMinify), nameof(IsJson), nameof(IsCases), nameof(IsUtm))]
    public partial WebHelper Helper { get; set; }

    [ObservableProperty] public partial string WebMessage { get; set; } = "";

    public bool IsLorem => Helper == WebHelper.Lorem;
    public bool IsPlaceholder => Helper == WebHelper.Placeholder;
    public bool IsUnits => Helper == WebHelper.Units;
    public bool IsMinify => Helper == WebHelper.Minify;
    public bool IsJson => Helper == WebHelper.Json;
    public bool IsCases => Helper == WebHelper.Cases;
    public bool IsUtm => Helper == WebHelper.Utm;

    partial void OnHelperChanged(WebHelper value) => WebMessage = "";

    [RelayCommand]
    private void ShowHelper(WebHelper helper) => Helper = helper;

    /// <summary>Copies one of the answers; the button passes the text and the view its window.</summary>
    public async Task CopyWebAsync(TopLevel? top, string? text, string what)
    {
        if (string.IsNullOrEmpty(text) || top?.Clipboard is not { } clipboard)
            return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        WebMessage = what + " copied.";
    }

    // Filler text

    public IReadOnlyList<string> LoremKinds { get; } = ["Paragraphs", "Sentences", "Words"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoremText), nameof(LoremMost))]
    public partial string LoremKindChoice { get; set; } = "Paragraphs";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoremText))]
    public partial decimal? LoremCount { get; set; } = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoremText))]
    public partial bool LoremClassicStart { get; set; } = true;

    private LoremKind LoremKindPicked => (LoremKind)Math.Max(0, LoremKinds.ToList().IndexOf(LoremKindChoice));

    public int LoremMost => LoremIpsum.Most(LoremKindPicked);
    public string LoremText => LoremIpsum.Make(LoremKindPicked, (int)(LoremCount ?? 1), LoremClassicStart);

    // Placeholder picture

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceholderLabel), nameof(PreviewWidth), nameof(PreviewHeight), nameof(PreviewFont), nameof(PlaceholderName))]
    public partial decimal? PlaceholderWidth { get; set; } = 1200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceholderLabel), nameof(PreviewWidth), nameof(PreviewHeight), nameof(PreviewFont), nameof(PlaceholderName))]
    public partial decimal? PlaceholderHeight { get; set; } = 630;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceholderLabel), nameof(PreviewFont))]
    public partial string PlaceholderText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceholderBrush), nameof(PlaceholderInk), nameof(PlaceholderColourProblem))]
    public partial string PlaceholderColour { get; set; } = Placeholders.UsualColour;

    public int PlaceholderMost => Placeholders.Most;

    private (int Width, int Height) PlaceholderSize => Placeholders.Size((int)(PlaceholderWidth ?? 1), (int)(PlaceholderHeight ?? 1));
    private Rgb PlaceholderRgb => Colours.Parse(PlaceholderColour) ?? Colours.Parse(Placeholders.UsualColour)!.Value;

    // The preview fits a box of this size, keeping the picture's shape.
    private double PreviewScale => Math.Min(1, Math.Min(300.0 / PlaceholderSize.Width, 190.0 / PlaceholderSize.Height));

    public string PlaceholderLabel => Placeholders.Label(PlaceholderSize.Width, PlaceholderSize.Height, PlaceholderText);
    public string PlaceholderName => Placeholders.FileName(PlaceholderSize.Width, PlaceholderSize.Height);
    public double PreviewWidth => Math.Max(1, PlaceholderSize.Width * PreviewScale);
    public double PreviewHeight => Math.Max(1, PlaceholderSize.Height * PreviewScale);
    public double PreviewFont => Math.Max(1, Placeholders.FontSize(PlaceholderSize.Width, PlaceholderSize.Height, PlaceholderLabel) * PreviewScale);
    public IBrush PlaceholderBrush => Brush(PlaceholderRgb);
    public IBrush PlaceholderInk => Brush(Placeholders.Ink(PlaceholderRgb));
    public bool PlaceholderColourProblem => Colours.Parse(PlaceholderColour) is null;

    private static SolidColorBrush Brush(Rgb c) => new(Color.FromRgb(c.R, c.G, c.B));

    /// <summary>The placeholder at its full size, as PNG.</summary>
    public byte[] PlaceholderPng()
    {
        var (width, height) = PlaceholderSize;
        var label = PlaceholderLabel;
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            context.FillRectangle(PlaceholderBrush, new Rect(0, 0, width, height));
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default,
                Placeholders.FontSize(width, height, label), PlaceholderInk);
            context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
        }
        using var stream = new MemoryStream();
        target.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public void PlaceholderSaved(string name) => WebMessage = $"Saved as {name}.";

    // px and rem

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitAnswer), nameof(UnitSum), nameof(HasUnitAnswer), nameof(UnitHint))]
    public partial string UnitInput { get; set; } = "24px";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitAnswer), nameof(UnitSum), nameof(HasUnitAnswer), nameof(UnitHint))]
    public partial string UnitBase { get; set; } = "16";

    public string UnitAnswer => CssUnits.Base(UnitBase) is { } basePx ? CssUnits.Convert(UnitInput, basePx) ?? "" : "";
    public string UnitSum => CssUnits.Base(UnitBase) is { } basePx ? CssUnits.Sum(UnitInput, basePx) ?? "" : "";
    public bool HasUnitAnswer => UnitAnswer.Length > 0;

    public string UnitHint =>
        CssUnits.Base(UnitBase) is null ? "The base size is the site's root font size in pixels, usually 16."
        : UnitInput.Trim().Length > 0 && !HasUnitAnswer ? "Type a size such as 24px or 1.5rem."
        : "";

    // Smaller CSS and JavaScript

    public IReadOnlyList<string> MinifyKinds { get; } = ["CSS", "JavaScript"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinifyOutput), nameof(MinifySaved), nameof(HasMinifyOutput), nameof(MinifyNote))]
    public partial string MinifyKindChoice { get; set; } = "CSS";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinifyOutput), nameof(MinifySaved), nameof(HasMinifyOutput))]
    public partial string MinifyInput { get; set; } = "";

    private bool MinifyIsCss => MinifyKindChoice == "CSS";

    public string MinifyOutput => MinifyIsCss ? Minify.Css(MinifyInput) : Minify.Js(MinifyInput);
    public bool HasMinifyOutput => MinifyOutput.Length > 0;

    // Counts shown the PC's way; the code itself is never formatted.
    public string MinifySaved => HasMinifyOutput
        ? $"{MinifyInput.Length:N0} characters down to {MinifyOutput.Length:N0}, {Minify.Saved(MinifyInput, MinifyOutput)}% smaller."
        : "";

    public string MinifyNote => MinifyIsCss
        ? "Comments and spare space come out. Nothing is renamed or rewritten, so it works as before."
        : "Comments, indents and empty lines come out. Line breaks between statements stay, as JavaScript reads meaning into them, and nothing is renamed, so it runs as before.";

    // JSON

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonOutput), nameof(JsonProblem), nameof(HasJsonOutput), nameof(HasJsonProblem))]
    public partial string JsonInput { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonOutput), nameof(JsonLaidOut))]
    public partial bool JsonOneLine { get; set; }

    public bool JsonLaidOut => !JsonOneLine;

    private JsonTidied JsonResult => JsonTidy.Tidy(JsonInput, JsonOneLine);

    public string JsonOutput => JsonResult.Text;
    public string JsonProblem => JsonResult.Problem ?? "";
    public bool HasJsonOutput => JsonOutput.Length > 0;
    public bool HasJsonProblem => JsonProblem.Length > 0;

    [RelayCommand]
    private void JsonLayout(string oneLine) => JsonOneLine = oneLine == "one";

    // Slugs and cases

    [ObservableProperty] public partial string CaseInput { get; set; } = "";

    public ObservableCollection<TextCase> Cases { get; } = [];

    public bool HasCases => Cases.Count > 0;

    partial void OnCaseInputChanged(string value)
    {
        Cases.Clear();
        foreach (var one in TextCases.All(value))
            Cases.Add(one);
        OnPropertyChanged(nameof(HasCases));
    }

    // Campaign link

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtmLink), nameof(UtmProblem), nameof(HasUtmLink), nameof(HasUtmProblem))]
    public partial string UtmAddress { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtmLink), nameof(UtmProblem), nameof(HasUtmLink), nameof(HasUtmProblem))]
    public partial string UtmSource { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtmLink))]
    public partial string UtmMedium { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtmLink))]
    public partial string UtmCampaign { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtmLink))]
    public partial string UtmTerm { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtmLink))]
    public partial string UtmContent { get; set; } = "";

    private UtmLink UtmResult => UtmLinks.Build(UtmAddress, UtmSource, UtmMedium, UtmCampaign, UtmTerm, UtmContent);

    public string UtmLink => UtmResult.Link;
    public string UtmProblem => UtmResult.Problem ?? "";
    public bool HasUtmLink => UtmLink.Length > 0;
    public bool HasUtmProblem => UtmProblem.Length > 0;
}
