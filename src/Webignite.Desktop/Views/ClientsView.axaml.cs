using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class ClientsView : UserControl
{
    public ClientsView() => InitializeComponent();

    private void PaneResized(string pane, double width) => (DataContext as ClientsViewModel)?.Panels.Resized(pane, width);
}
