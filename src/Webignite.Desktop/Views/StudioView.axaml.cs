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
        DataContextChanged += (_, _) => Follow();
    }

    private StudioViewModel? followed;

    // The lines are drawn only while they are the picture showing, and draw themselves in afresh each time.
    private void Follow()
    {
        if (followed is not null)
            followed.PropertyChanged -= PageChanged;
        followed = DataContext as StudioViewModel;
        if (followed is not null)
            followed.PropertyChanged += PageChanged;
        ShowLines();
        ShowWall();
    }

    private void ShowWall()
    {
        if (followed is not { IsWall: true } page)
        {
            WallHost.Content = null;
            return;
        }
        var wall = new StudioWallPanel();
        wall.Bind(StudioWallPanel.CardsProperty, new Avalonia.Data.Binding(nameof(StudioViewModel.Wall)) { Source = page });
        wall.Opened += page.Open;
        WallHost.Content = wall;
    }

    /// <summary>The wall, when it is showing, so a picture of it can be taken with its cards in place.</summary>
    public StudioWallPanel? WallPanel => WallHost.Content as StudioWallPanel;

    private void PageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioViewModel.Show))
        {
            ShowLines();
            ShowWall();
        }
    }

    private void ShowLines()
    {
        if (followed is not { IsLines: true } page)
        {
            LinesHost.Content = null;
            return;
        }
        var lines = new StudioLinesCanvas();
        lines.Bind(StudioLinesCanvas.LinesProperty, new Avalonia.Data.Binding(nameof(StudioViewModel.Lines)) { Source = page });
        lines.Opened += page.Open;
        LinesHost.Content = lines;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (followed is not null)
            followed.PropertyChanged -= PageChanged;
        followed = null;
        LinesHost.Content = null;
        WallHost.Content = null;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow();
    }

    /// <summary>The lines, when they are showing, so a picture of them can be taken with the drawing-in at its end.</summary>
    public StudioLinesCanvas? LinesCanvas => LinesHost.Content as StudioLinesCanvas;

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
        var shown = new StudioViewModel(services, presenting: true, demo: page.Demo, show: page.Show);
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
