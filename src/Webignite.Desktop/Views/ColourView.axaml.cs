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
}
