using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Screen ruler: starts a measurement and lists what was measured while the window is open.</summary>
public partial class RulerView : UserControl
{
    public RulerView() => InitializeComponent();

    // Copies the plain size, "480 × 270", without the "px" and the at-100% part.
    private void CopyRow(object? sender, RoutedEventArgs e)
    {
        var words = (sender as Control)?.Tag as string ?? "";
        var plain = words.Split(" px", 2)[0];
        ((DataContext as ToolsViewModel)?.CopyWebAsync(TopLevel.GetTopLevel(this), plain, "Size") ?? Task.CompletedTask).Forget("Copying a size", tell: true);
    }
}
