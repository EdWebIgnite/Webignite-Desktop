using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Text from the screen: starts a pick and shows the words that were read, to check and copy from.</summary>
public partial class ScreenTextView : UserControl
{
    public ScreenTextView() => InitializeComponent();

    private void CopyAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel tools)
            tools.CopyWebAsync(TopLevel.GetTopLevel(this), tools.ScreenTextRead, "Text").Forget("Copying text read off the screen", tell: true);
    }
}
