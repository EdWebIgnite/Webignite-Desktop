using Avalonia.Controls;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class DocumentEditorView : UserControl
{
    public DocumentEditorView() => InitializeComponent();

    // "3.5 x 650" becomes 2275 when the price or quantity box is left.
    private void SettleSum(object? sender, RoutedEventArgs e) => ((sender as Control)?.DataContext as LineRowViewModel)?.Settle();
}
