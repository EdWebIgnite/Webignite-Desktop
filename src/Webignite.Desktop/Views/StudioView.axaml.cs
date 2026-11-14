using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The Studio page. Present opens a copy of it filling the screen, which Esc closes.</summary>
public partial class StudioView : UserControl
{
    public StudioView()
    {
        InitializeComponent();
        Map.Opened += star => (DataContext as StudioViewModel)?.Open(star);
    }

    /// <summary>The map itself, so a picture of it can be taken with every movement at its end.</summary>
    public StudioMapCanvas Canvas => Map;

    /// <summary>For the copy that fills the screen: the map runs to the edges.</summary>
    public StudioView Edgeless()
    {
        Frame.Margin = default;
        return this;
    }

    private void Present(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StudioViewModel page || App.Services is not { } services)
            return;
        var shown = new StudioViewModel(services, presenting: true, demo: page.Demo);
        var window = new Window
        {
            Title = "Studio",
            WindowState = WindowState.FullScreen,
            Content = new StudioView { DataContext = shown, Padding = default }.Edgeless(),
        };
        // Esc with nothing picked closes; with a client picked the map takes it first and flies back out.
        window.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Escape && !key.Handled)
                window.Close();
        };
        window.Closed += (_, _) => shown.Dispose();
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }
}
