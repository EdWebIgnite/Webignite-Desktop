using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Colours: type one or pick it from anywhere on the screen, with its shades and how it reads on white and black.</summary>
public partial class ColourView : UserControl
{
    private ToolsViewModel? watching;

    public ColourView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        watching = DataContext as ToolsViewModel;
        watching?.WatchColours();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        watching?.StopColours();
        watching = null;
        base.OnDetachedFromVisualTree(e);
    }

    // The same eyedropper as the shortcut: the screen freezes and the colour clicked comes back here.
    private void PickFromScreen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel tools && App.Services is { } services)
            services.Go.PickColour(hex => tools.ColourInput = hex);
    }

    private void ContrastTextTyped(object? sender, Avalonia.Input.KeyEventArgs e) => (DataContext as ToolsViewModel)?.ContrastTextWasTyped();

    private void PaletteFromPicture(object? sender, RoutedEventArgs e) => FromPictureAsync().Forget("Reading a picture's colours", tell: true);

    // A photo or logo's main colours; for an SVG, the colours written in it.
    private async Task FromPictureAsync()
    {
        if (DataContext is not ToolsViewModel tools)
            return;
        var picked = (await Files.Picker.Files(this, "A logo or photo", ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.svg"], kindName: "Pictures")).FirstOrDefault();
        if (picked is null)
            return;
        var name = System.IO.Path.GetFileName(picked);
        try
        {
            var colours = await Task.Run(() => ToolsViewModel.PictureColours(picked));
            tools.AddToPalette(colours, name);
        }
        catch (Exception problem) when (problem is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            tools.AddToPalette([], name);
        }
    }
}
